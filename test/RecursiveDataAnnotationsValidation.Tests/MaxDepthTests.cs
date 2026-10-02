using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// The maximum depth of the object graph is 128.
    ///
    /// The walk calls itself once for each level, so a deep graph is a deep call stack. .NET cannot
    /// catch a StackOverflowException (https://learn.microsoft.com/dotnet/api/system.stackoverflowexception),
    /// so the process ends. A graph this deep is not a real model. It points to a cycle that the
    /// validator cannot see, such as a computed property that returns a new object on each read,
    /// or to a graph that was built from untrusted input. The validator therefore fails the
    /// validation and does not walk any further.
    ///
    /// The depth of an object is the number of segments in its path. In "Value[0][0].Name",
    /// Value is level 1, the first [0] is level 2, the second [0] is level 3 and Name is level 4.
    /// Each property step and each collection index counts as one level. System.Text.Json counts
    /// nearly the same way, where each object and each array is one level, and its limit is 64
    /// (https://learn.microsoft.com/dotnet/api/system.text.json.jsonserializeroptions.maxdepth).
    /// A document within that limit stays within about 63 levels here. The root object is level 0.
    /// Our limit is twice the JSON limit.
    /// An object at level 128 is validated. An object at level 129 is not validated, and its path
    /// gets one error that says so. A collection of leaf types is not walked, so it never counts.
    ///
    /// A tree of objects that holds its children in a List has two levels for each tree level,
    /// one for the property and one for the index, so the tree can be 64 levels deep.
    ///
    /// A graph that is too deep fails the validation. It does not throw, and it does not stop
    /// silently, because a graph that nobody validated must not pass.
    ///
    /// The limit is internal and fixed. It is not a setting.
    /// </summary>
    public class MaxDepthTests
    {
        private const int OneMegabyte = 1024 * 1024;

        private const string TooDeep = "The object is nested more than 128 levels deep and was not validated.";

        public class Node
        {
            public Node Next { get; set; }

            [Required(ErrorMessage = "Name is required")]
            public string Name { get; set; }
        }

        public class TreeNode
        {
            public List<TreeNode> Children { get; set; } = new List<TreeNode>();

            [Required(ErrorMessage = "Name is required")]
            public string Name { get; set; }
        }

        public class Holder
        {
            public List<object> Value { get; set; }
        }

        /// <summary>
        /// Builds a new object on each read and does not override Equals, so no object ever
        /// matches an object on its path and the reference check never sees a repeat.
        /// </summary>
        public class Vector
        {
            public Vector Zero => new Vector();
        }

        // A chain of `levels` objects below the root. The object at the bottom has the given name.
        private static Node Chain(int levels, string bottomName)
        {
            var root = new Node { Name = levels == 0 ? bottomName : "ok" };
            var current = root;
            for (var i = 0; i < levels; i++)
            {
                current.Next = new Node { Name = i == levels - 1 ? bottomName : "ok" };
                current = current.Next;
            }

            return root;
        }

        // The path of the object at the bottom of a chain: "Next" repeated.
        private static string NextPath(int levels) =>
            string.Join(".", Enumerable.Repeat("Next", levels));

        // A tree with one child on each node, `levels` below the root.
        private static TreeNode Tree(int levels, string bottomName)
        {
            var root = new TreeNode { Name = levels == 0 ? bottomName : "ok" };
            var current = root;
            for (var i = 0; i < levels; i++)
            {
                var child = new TreeNode { Name = i == levels - 1 ? bottomName : "ok" };
                current.Children.Add(child);
                current = child;
            }

            return root;
        }

        private static string ChildrenPath(int levels) =>
            string.Join(".", Enumerable.Repeat("Children[0]", levels));

        // A Node that is invalid, inside `lists` lists that each hold the next one: Value[0][0]...Name.
        private static Holder NestedLists(int lists)
        {
            object inner = new Node { Name = null };
            for (var i = 0; i < lists; i++)
            {
                inner = new List<object> { inner };
            }

            return new Holder { Value = (List<object>)inner };
        }

        private static string ValueIndexes(int count) =>
            "Value" + string.Concat(Enumerable.Repeat("[0]", count));

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        public class ObjectsInAChain
        {
            [Fact]
            public void Object_at_level_128_is_validated()
            {
                var (valid, errors) = Run(Chain(128, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(128)}.Name | Name is required"), errors);
            }

            /// <summary>
            /// The string at level 129 is a leaf: it can never produce a result, so nothing is
            /// walked there and the limit does not apply.
            /// </summary>
            [Fact]
            public void Valid_chain_with_its_last_object_at_level_128_passes()
            {
                var (valid, errors) = Run(Chain(128, "ok"));

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Valid_object_at_level_129_fails_the_validation()
            {
                var (valid, errors) = Run(Chain(129, "ok"));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(129)} | {TooDeep}"), errors);
            }

            /// <summary>
            /// The object at level 129 is not validated, so its own errors are not reported.
            /// The one error says why.
            /// </summary>
            [Fact]
            public void Invalid_object_at_level_129_gets_only_the_depth_error()
            {
                var (valid, errors) = Run(Chain(129, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(129)} | {TooDeep}"), errors);
            }

            [Fact]
            public void Walk_stops_at_the_first_object_that_is_too_deep()
            {
                var (valid, errors) = Run(Chain(500, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(129)} | {TooDeep}"), errors);
            }

            [Fact]
            public void Errors_above_the_limit_are_still_reported()
            {
                var root = Chain(129, "ok");
                root.Name = null;

                var (valid, errors) = Run(root);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Name | Name is required",
                        $"{NextPath(129)} | {TooDeep}"),
                    errors);
            }

            [Fact]
            public void A_null_results_list_still_gets_false()
            {
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(Chain(129, "ok"), null);

                Assert.False(valid);
            }

            [Fact]
            public async Task The_async_method_fails_the_same_way()
            {
                var results = new List<ValidationResult>();

                var valid = await new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursiveAsync(Chain(129, "ok"), results);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(129)} | {TooDeep}"), ResultText.Describe(results));
            }

            /// <summary>
            /// Before the limit, a chain of about 1,300 objects was the most that a 1 MB stack, the
            /// default on Windows, could walk. A chain of 600 is well below that, so this test cannot
            /// end the test run when the limit is missing, and it still shows that the validator now
            /// fails the validation at level 129. A chain of 100,000 would end the process.
            /// </summary>
            [Fact]
            public void A_chain_of_600_objects_fails_on_a_small_stack()
            {
                var results = new List<ValidationResult>();
                var valid = true;
                Exception error = null;

                var thread = new Thread(() =>
                {
                    try
                    {
                        valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(Chain(600, "ok"), results);
                    }
                    catch (Exception e)
                    {
                        error = e;
                    }
                }, OneMegabyte);

                thread.Start();
                thread.Join();

                if (error != null) ExceptionDispatchInfo.Capture(error).Throw();

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(129)} | {TooDeep}"), ResultText.Describe(results));
            }
        }

        public class ComputedProperties
        {
            /// <summary>
            /// Before the limit, this walk never ended and overflowed the stack, which ends the
            /// process. A type that overrides Equals is stopped earlier (see EqualsAnAncestor), and
            /// a type that does not override it is stopped by the limit.
            /// </summary>
            [Fact]
            public void Property_that_returns_a_new_object_on_each_read_fails_at_the_limit()
            {
                var (valid, errors) = Run(new Vector());

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{string.Join(".", Enumerable.Repeat("Zero", 129))} | {TooDeep}"), errors);
            }
        }

        public class ObjectsInCollections
        {
            /// <summary>
            /// Each tree level is two path levels: the Children property and the index.
            /// The 64th child is at level 128.
            /// </summary>
            [Fact]
            public void Tree_64_levels_deep_is_validated_to_the_bottom()
            {
                var (valid, errors) = Run(Tree(64, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChildrenPath(64)}.Name | Name is required"), errors);
            }

            [Fact]
            public void Valid_tree_64_levels_deep_passes()
            {
                var (valid, errors) = Run(Tree(64, "ok"));

                Assert.True(valid);
                Assert.Empty(errors);
            }

            /// <summary>
            /// The 65th child is at level 130. The path of the error ends with the index, not with
            /// a member, because the object at that path was not validated.
            /// </summary>
            [Fact]
            public void Tree_65_levels_deep_fails_at_the_65th_child()
            {
                var (valid, errors) = Run(Tree(65, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChildrenPath(65)} | {TooDeep}"), errors);
            }

            [Fact]
            public void Every_item_that_is_too_deep_gets_its_own_error()
            {
                var node = Tree(64, "ok");
                var bottom = node;
                while (bottom.Children.Count > 0) bottom = bottom.Children[0];
                bottom.Children.Add(new TreeNode { Name = "ok" });
                bottom.Children.Add(new TreeNode { Name = "ok" });

                var (valid, errors) = Run(node);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        $"{ChildrenPath(64)}.Children[0] | {TooDeep}",
                        $"{ChildrenPath(64)}.Children[1] | {TooDeep}"),
                    errors);
            }
        }

        /// <summary>
        /// An item that is a collection counts one level for its own index, like any other item.
        /// The path of the object inside the lists is the path of the lists, as in "Value[0][0].Name".
        /// </summary>
        public class ObjectsInCollectionsOfCollections
        {
            [Fact]
            public void Object_at_level_128_is_validated()
            {
                var (valid, errors) = Run(NestedLists(127));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ValueIndexes(127)}.Name | Name is required"), errors);
            }

            [Fact]
            public void Object_at_level_129_is_not_validated_and_the_path_ends_with_its_index()
            {
                var (valid, errors) = Run(NestedLists(128));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ValueIndexes(128)} | {TooDeep}"), errors);
            }

            /// <summary>
            /// A list nested more than 128 levels deep is not a stack risk any more. Before the limit,
            /// 1,200 nested lists overflowed a 1 MB stack, and in a Debug build, which uses more stack
            /// for each level, 1,000 lists overflowed the test host. A depth of 300 is safe in both
            /// cases, and it is more than twice the limit.
            /// </summary>
            [Fact]
            public void Three_hundred_nested_lists_fail_the_validation()
            {
                var (valid, errors) = Run(NestedLists(300));

                Assert.False(valid);
                Assert.Single(errors);
                Assert.EndsWith($" | {TooDeep}", errors[0]);
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// A struct collection that a property holds, such as ImmutableArray. It counts like any
        /// other collection: one level for the property and one for the index. System.Collections.Immutable
        /// is not part of net481.
        /// </summary>
        public class StructCollectionProperties
        {
            public class ImmutableNode
            {
                public System.Collections.Immutable.ImmutableArray<ImmutableNode> Children { get; set; }

                [Required(ErrorMessage = "Name is required")]
                public string Name { get; set; }
            }

            // Builds from the bottom: a chain of `levels` nodes below the root.
            private static ImmutableNode ImmutableTree(int levels, string bottomName)
            {
                var node = new ImmutableNode { Name = bottomName };
                for (var i = 0; i < levels; i++)
                {
                    node = new ImmutableNode
                    {
                        Name = "ok",
                        Children = System.Collections.Immutable.ImmutableArray.Create(node),
                    };
                }

                return node;
            }

            /// <summary>
            /// A property that builds a new struct collection of new objects on each read. Before the
            /// limit, this overflowed the stack, which ends the process.
            /// </summary>
            public class Spawner
            {
                public System.Collections.Immutable.ImmutableArray<Spawner> Next =>
                    System.Collections.Immutable.ImmutableArray.Create(new Spawner());
            }

            [Fact]
            public void Tree_64_levels_deep_is_validated_to_the_bottom()
            {
                var (valid, errors) = Run(ImmutableTree(64, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChildrenPath(64)}.Name | Name is required"), errors);
            }

            [Fact]
            public void Tree_65_levels_deep_fails_at_the_65th_child()
            {
                var (valid, errors) = Run(ImmutableTree(65, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChildrenPath(65)} | {TooDeep}"), errors);
            }

            [Fact]
            public void Property_that_builds_a_new_struct_collection_on_each_read_fails_at_the_limit()
            {
                var (valid, errors) = Run(new Spawner());

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect($"{string.Join(".", Enumerable.Repeat("Next[0]", 65))} | {TooDeep}"),
                    errors);
            }
        }
#endif
    }
}
