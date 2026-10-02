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
    /// Large graphs: deep chains and wide lists. Real models are shallow, but a linked list, a
    /// comment thread or a category tree can be dozens of levels deep, and an import can hold
    /// thousands of items.
    ///
    /// Up to 2.3 the walk called itself once for each level, so the depth of the graph was the depth
    /// of the call stack. Since 3.0 the walk keeps its work in a queue and uses no stack for the
    /// depth of the graph. These tests still run the walk on a thread with a 1 MB stack, which is
    /// the default on Windows, so a walk that calls itself again fails here. macOS and Linux give
    /// the main thread 8 MB, so a test that runs on the default stack would not show that.
    /// See: https://learn.microsoft.com/dotnet/api/system.threading.thread.-ctor
    ///
    /// The validator has a maximum depth of 128 (see MaxDepthTests), which these tests stay below.
    /// Before the limit, a chain of about 1,300 objects passed on a 1 MB stack and a chain of about
    /// 1,600 overflowed it, in both Debug and Release builds on .NET 8 (measured on macOS). The
    /// numbers change with the runtime and the size of the objects. .NET cannot catch a
    /// StackOverflowException, so the process ends.
    /// See: https://learn.microsoft.com/dotnet/api/system.stackoverflowexception
    ///
    /// The tests only use the public API, so they also pass against the validator of v2.2.0.
    /// A chain of 120 objects is deeper than a real model and below the limit. A tree that holds
    /// its children in a List has two levels for each tree level, so it is 60 levels deep.
    /// </summary>
    public class GraphSizeTests
    {
        private const int Depth = 120;

        private const int TreeDepth = 60;

        private const int OneMegabyte = 1024 * 1024;

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

        /// <summary>
        /// A class that overrides Equals by Id. The validator compares each object with the objects
        /// above it on the path (see EqualsAnAncestor), so the number of Equals calls grows with
        /// the square of the depth. With distinct ids none of them is equal, and the walk goes on.
        /// </summary>
        public class IdNode
        {
            public int Id { get; set; }

            public IdNode Next { get; set; }

            [Required(ErrorMessage = "Name is required")]
            public string Name { get; set; }

            public override bool Equals(object obj) => obj is IdNode other && other.Id == Id;

            public override int GetHashCode() => Id;
        }

        public class Batch
        {
            public List<Node> Items { get; set; } = new List<Node>();
        }

        // Runs the walk on a thread with a 1 MB stack and returns what the caller would see.
        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = false;
            Exception error = null;

            var thread = new Thread(() =>
            {
                try
                {
                    valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
                }
                catch (Exception e)
                {
                    error = e;
                }
            }, OneMegabyte);

            thread.Start();
            thread.Join();

            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();

            return (valid, ResultText.Describe(results));
        }

        // The path of a chain: "Next" repeated, then the member, such as "Next.Next.Name".
        private static string ChainPath(int levels, string member) =>
            string.Join(".", Enumerable.Repeat("Next", levels).Concat(new[] { member }));

        private static Node Chain(int levels, string bottomName)
        {
            var root = new Node { Name = "ok" };
            var current = root;
            for (var i = 0; i < levels; i++)
            {
                current.Next = new Node { Name = i == levels - 1 ? bottomName : "ok" };
                current = current.Next;
            }

            return root;
        }

        public class DeepChains
        {
            [Fact]
            public void Valid_chain_passes()
            {
                var (valid, errors) = Run(Chain(Depth, "ok"));

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Error_at_the_bottom_has_the_full_path()
            {
                var (valid, errors) = Run(Chain(Depth, null));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChainPath(Depth, "Name")} | Name is required"), errors);
            }

            [Fact]
            public async Task Error_at_the_bottom_has_the_full_path_in_the_async_method()
            {
                // Task.Run uses a thread-pool thread, whose stack size the caller cannot choose.
                // A depth of 120 is below the maximum depth.
                var results = new List<ValidationResult>();

                var valid = await new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursiveAsync(Chain(Depth, null), results);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChainPath(Depth, "Name")} | Name is required"), ResultText.Describe(results));
            }

            [Fact]
            public void Chain_through_collections_has_the_full_path()
            {
                var root = new TreeNode { Name = "ok" };
                var current = root;
                for (var i = 0; i < TreeDepth; i++)
                {
                    var child = new TreeNode { Name = i == TreeDepth - 1 ? null : "ok" };
                    current.Children.Add(child);
                    current = child;
                }

                var (valid, errors) = Run(root);

                var path = string.Join(".", Enumerable.Repeat("Children[0]", TreeDepth).Concat(new[] { "Name" }));
                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{path} | Name is required"), errors);
            }

            /// <summary>
            /// Every object overrides Equals, so the validator keeps the path and compares each
            /// new object with every object above it. Distinct ids mean no object is skipped.
            /// </summary>
            [Fact]
            public void Chain_of_objects_with_an_Equals_override_has_the_full_path()
            {
                var root = new IdNode { Id = 0, Name = "ok" };
                var current = root;
                for (var i = 1; i <= Depth; i++)
                {
                    current.Next = new IdNode { Id = i, Name = i == Depth ? null : "ok" };
                    current = current.Next;
                }

                var (valid, errors) = Run(root);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{ChainPath(Depth, "Name")} | Name is required"), errors);
            }
        }

        public class WideGraphs
        {
            [Fact]
            public void Ten_thousand_items_report_each_invalid_item_by_index()
            {
                var batch = new Batch();
                for (var i = 0; i < 10_000; i++) batch.Items.Add(new Node { Name = "ok" });
                batch.Items[0].Name = null;
                batch.Items[5_000].Name = null;
                batch.Items[9_999].Name = null;

                var (valid, errors) = Run(batch);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Items[0].Name | Name is required",
                        "Items[5000].Name | Name is required",
                        "Items[9999].Name | Name is required"),
                    errors);
            }

            [Fact]
            public void Ten_thousand_valid_items_pass()
            {
                var batch = new Batch();
                for (var i = 0; i < 10_000; i++) batch.Items.Add(new Node { Name = "ok" });

                var (valid, errors) = Run(batch);

                Assert.True(valid);
                Assert.Empty(errors);
            }

            /// <summary>
            /// A tree with three children on each node and six levels below the root has 1,093 nodes
            /// and 729 leaves. Every leaf is invalid, so the result has one error for each leaf.
            /// </summary>
            [Fact]
            public void Tree_with_every_leaf_invalid_reports_every_leaf()
            {
                TreeNode Build(int levelsBelow) => new TreeNode
                {
                    Name = levelsBelow == 0 ? null : "ok",
                    Children = levelsBelow == 0
                        ? new List<TreeNode>()
                        : Enumerable.Range(0, 3).Select(_ => Build(levelsBelow - 1)).ToList(),
                };

                var (valid, errors) = Run(Build(6));

                Assert.False(valid);
                Assert.Equal(729, errors.Count);
                Assert.Equal(729, errors.Distinct().Count());
                Assert.Contains("Children[0].Children[0].Children[0].Children[0].Children[0].Children[0].Name | Name is required", errors);
                Assert.Contains("Children[2].Children[2].Children[2].Children[2].Children[2].Children[2].Name | Name is required", errors);
            }
        }
    }
}
