using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Attributes;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// The upgrade contract from 2.2 to 2.3: for the models callers write every day, 2.3 returns
    /// the same true/false answer and the same results as 2.2, with the same member names and
    /// messages, in the same order.
    ///
    /// Every test here passes against the validator source of both v2.2.0 and v2.2.4. To check
    /// that again, copy the old source over the current one, run only this class, then restore it:
    ///   git show v2.2.0:src/RecursiveDataAnnotationsValidation/RecursiveDataAnnotationValidator.cs \
    ///     > src/RecursiveDataAnnotationsValidation/RecursiveDataAnnotationValidator.cs
    ///   dotnet test test/RecursiveDataAnnotationsValidation.Tests --filter "FullyQualifiedName~UpgradeCompatibilityTests"
    ///   git checkout -- src/RecursiveDataAnnotationsValidation/RecursiveDataAnnotationValidator.cs
    ///
    /// The tests leave out, on purpose, the shapes where 2.3 differs from 2.2. CHANGELOG.md lists
    /// them, and other test classes cover them:
    /// - Separate objects that are Equals, such as two equal records (ValidatorHardeningTests).
    /// - A null results list, and the service provider of the context (ConsumerUseCaseTests).
    /// - Framework types such as Type, delegates and relative Uris (ValidatorHardeningTests).
    /// - Lazy sequences of primitives, which 2.3 no longer runs (ValidatorHardeningTests).
    /// - A null object or context, and a property hidden with `new` (fixed in 2.2.4).
    ///
    /// The messages are the framework's default English messages, or a custom ErrorMessage where
    /// .NET Framework and .NET might word a default differently.
    /// </summary>
    public class UpgradeCompatibilityTests
    {
        private static bool Validate(object obj, out List<string> results)
        {
            var list = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(obj, list);
            results = ResultText.Describe(list);
            return valid;
        }

        public class Leaf
        {
            [Required]
            public string Name { get; set; }
        }

        /// <summary>The true/false answer and the shape of the results list.</summary>
        public class Results
        {
            public class Address
            {
                [Display(Name = "Postal code")]
                [Required]
                public string Zip { get; set; }

                [Required(ErrorMessage = "Give the {0}, please.")]
                public string City { get; set; }
            }

            public class Customer
            {
                [Required]
                public string Name { get; set; }

                [Range(1, 10)]
                public int Rank { get; set; }

                public Address Address { get; set; }

                [Required]
                public string Email { get; set; }
            }

            [Fact]
            public void Valid_graph_returns_true_and_adds_no_results()
            {
                var customer = new Customer
                {
                    Name = "Ann", Rank = 1, Email = "a@example.com",
                    Address = new Address { Zip = "12345", City = "Paris" },
                };

                Assert.True(Validate(customer, out var results));
                Assert.Empty(results);
            }

            [Fact]
            public void Invalid_graph_returns_false_and_adds_every_result()
            {
                var customer = new Customer { Name = null, Rank = 0, Email = null, Address = new Address() };

                Assert.False(Validate(customer, out var results));
                Assert.Equal(ResultText.Expect(
                    "Name | The Name field is required.",
                    "Rank | The field Rank must be between 1 and 10.",
                    "Email | The Email field is required.",
                    "Address.Zip | The Postal code field is required.",
                    "Address.City | Give the City, please."),
                    results);
            }

            [Fact]
            public void Results_already_in_the_list_are_kept()
            {
                var list = new List<ValidationResult> { new ValidationResult("from the caller") };

                var valid = new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursive(new Customer { Name = "Ann", Rank = 1 }, list);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect(
                    " | from the caller",
                    "Email | The Email field is required."),
                    ResultText.Describe(list));
            }

            [Fact]
            public void A_valid_object_returns_true_even_when_the_list_already_has_results()
            {
                var list = new List<ValidationResult> { new ValidationResult("from the caller") };

                var valid = new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursive(new Leaf { Name = "n" }, list);

                Assert.True(valid);
                Assert.Single(list);
            }

            /// <summary>
            /// Callers who show errors in a list see them in this order: the root object's own
            /// results first, as the framework's Validator returns them, then the results of the
            /// objects one level below it, in the order reflection lists the properties, then the
            /// objects two levels below it. The walk is breadth first since 3.0, so the shallowest
            /// objects come first. Up to 2.3 it was depth first, and the items of Items came
            /// between First and Last. An item of a collection is two levels below its object, one
            /// for the property and one for the index, so it comes after Last, which is one level
            /// below. Reflection does not promise declaration order, but every release makes the
            /// same GetProperties call. The test runs on every target framework.
            /// See: https://learn.microsoft.com/dotnet/api/system.type.getproperties
            /// </summary>
            [Fact]
            public void Root_results_come_first_then_nested_results_in_property_order()
            {
                var root = new Ordered
                {
                    First = new Leaf(),
                    Items = new List<Leaf> { new Leaf(), new Leaf() },
                    Last = new Leaf(),
                };

                var list = new List<ValidationResult>();
                new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(root, list);

                Assert.Equal(
                    new[] { "A", "Z", "First.Name", "Last.Name", "Items[0].Name", "Items[1].Name" },
                    list.Select(r => string.Join(",", r.MemberNames)));
            }

            public class Ordered
            {
                [Required]
                public string A { get; set; }

                public Leaf First { get; set; }

                public List<Leaf> Items { get; set; }

                public Leaf Last { get; set; }

                [Required]
                public string Z { get; set; }
            }

            /// <summary>
            /// A nested object is validated with its own ValidationContext, so its message names
            /// only its own property, never the path. Only the member names get the path.
            /// </summary>
            [Fact]
            public void Nested_messages_name_only_the_nested_property()
            {
                var customer = new Customer { Name = "Ann", Rank = 1, Email = "e", Address = new Address { City = "c" } };

                Validate(customer, out var results);

                Assert.Equal(ResultText.Expect("Address.Zip | The Postal code field is required."), results);
            }

            [Fact]
            public void Nested_objects_are_validated_when_the_root_fails()
            {
                var customer = new Customer { Rank = 0, Address = new Address { Zip = "z" } };

                Validate(customer, out var results);

                Assert.Contains("Address.City | Give the City, please.", results);
            }

            /// <summary>
            /// A ValidationAttribute on a class runs with no member name, so its result has none.
            /// Once nested, the result still has no member names, so it carries no path.
            /// </summary>
            [Fact]
            public void Class_level_attribute_on_a_nested_object_has_no_member_name()
            {
                var holder = new Holder { Range = new DateRange { Start = 5, End = 1 } };

                Assert.False(Validate(holder, out var results));
                Assert.Equal(ResultText.Expect(" | Start must not be after End."), results);
            }

            [AttributeUsage(AttributeTargets.Class)]
            public class OrderedRangeAttribute : ValidationAttribute
            {
                public OrderedRangeAttribute() : base("Start must not be after End.") { }

                public override bool IsValid(object value) =>
                    !(value is DateRange r) || r.Start <= r.End;
            }

            [OrderedRange]
            public class DateRange
            {
                public int Start { get; set; }
                public int End { get; set; }
            }

            public class Holder
            {
                public DateRange Range { get; set; }
            }

            /// <summary>
            /// The framework's Validator checks property attributes first. When one fails, it
            /// returns without running class-level attributes or IValidatableObject.Validate. The
            /// recursive validator calls Validator on each object, so a nested object behaves the
            /// same way.
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject
            /// </summary>
            [Fact]
            public void Validate_of_a_nested_object_does_not_run_when_its_properties_fail()
            {
                var holder = new SelfCheckHolder { Check = new SelfCheck { Name = null } };

                Validate(holder, out var results);

                Assert.Equal(ResultText.Expect("Check.Name | The Name field is required."), results);
            }

            [Fact]
            public void Validate_of_a_nested_object_runs_when_its_properties_pass()
            {
                var holder = new SelfCheckHolder { Check = new SelfCheck { Name = "n" } };

                Assert.False(Validate(holder, out var results));
                Assert.Equal(ResultText.Expect("Check.Name | Validate ran."), results);
            }

            [Fact]
            public void Validate_of_a_collection_item_gets_the_item_path()
            {
                var holder = new SelfCheckHolder { Checks = new List<SelfCheck> { new SelfCheck { Name = "n" } } };

                Validate(holder, out var results);

                Assert.Equal(ResultText.Expect("Checks[0].Name | Validate ran."), results);
            }

            public class SelfCheck : IValidatableObject
            {
                [Required]
                public string Name { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    yield return new ValidationResult("Validate ran.", new[] { nameof(Name) });
                }
            }

            public class SelfCheckHolder
            {
                public SelfCheck Check { get; set; }
                public List<SelfCheck> Checks { get; set; }
            }
        }

        /// <summary>The four public methods give the same answer for the same graph.</summary>
        public class Overloads
        {
            public enum Overload
            {
                Context,
                Items,
                ContextAsync,
                ItemsAsync,
            }

            public class Settings
            {
                [Required]
                public string Name { get; set; }

                [MaxFromItems]
                public string Code { get; set; }

                public Settings Child { get; set; }

                public List<Settings> Items { get; set; }
            }

            /// <summary>
            /// Reads the limit from ValidationContext.Items, so the test can see that the items
            /// reach every object in the graph.
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.items
            /// </summary>
            public class MaxFromItemsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    var max = validationContext.Items.TryGetValue("max", out var m) ? (int)m : int.MaxValue;
                    return value is string s && s.Length > max
                        ? new ValidationResult($"{validationContext.MemberName} is longer than {max}.", new[] { validationContext.MemberName })
                        : ValidationResult.Success;
                }
            }

            private static Settings InvalidGraph() => new Settings
            {
                Name = "root",
                Code = "toolong",
                Child = new Settings { Name = null, Code = "ok" },
                Items = new List<Settings>
                {
                    new Settings { Name = "a" },
                    new Settings { Name = null, Code = "longer" },
                },
            };

            private static async Task<bool> Run(
                Overload overload, object obj, List<ValidationResult> results, IDictionary<object, object> items)
            {
                var validator = new RecursiveDataAnnotationValidator();
                switch (overload)
                {
                    case Overload.Context:
                        return validator.TryValidateObjectRecursive(obj, new ValidationContext(obj, null, items), results);
                    case Overload.Items:
                        return validator.TryValidateObjectRecursive(obj, results, items);
                    case Overload.ContextAsync:
                        return await validator.TryValidateObjectRecursiveAsync(obj, new ValidationContext(obj, null, items), results);
                    default:
                        return await validator.TryValidateObjectRecursiveAsync(obj, results, items);
                }
            }

            [Theory]
            [InlineData(Overload.Context)]
            [InlineData(Overload.Items)]
            [InlineData(Overload.ContextAsync)]
            [InlineData(Overload.ItemsAsync)]
            public async Task Every_overload_gives_the_same_results(Overload overload)
            {
                var results = new List<ValidationResult>();

                var valid = await Run(overload, InvalidGraph(), results, new Dictionary<object, object> { ["max"] = 3 });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect(
                    "Code | Code is longer than 3.",
                    "Child.Name | The Name field is required.",
                    "Items[1].Name | The Name field is required.",
                    "Items[1].Code | Code is longer than 3."),
                    ResultText.Describe(results));
            }

            [Theory]
            [InlineData(Overload.Context)]
            [InlineData(Overload.Items)]
            [InlineData(Overload.ContextAsync)]
            [InlineData(Overload.ItemsAsync)]
            public async Task Every_overload_accepts_no_context_items(Overload overload)
            {
                var results = new List<ValidationResult>();

                var valid = await Run(overload, InvalidGraph(), results, null);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect(
                    "Child.Name | The Name field is required.",
                    "Items[1].Name | The Name field is required."),
                    ResultText.Describe(results));
            }

            [Theory]
            [InlineData(Overload.Context)]
            [InlineData(Overload.Items)]
            [InlineData(Overload.ContextAsync)]
            [InlineData(Overload.ItemsAsync)]
            public async Task Every_overload_returns_true_for_a_valid_graph(Overload overload)
            {
                var results = new List<ValidationResult>();
                var graph = new Settings { Name = "a", Child = new Settings { Name = "b" }, Items = new List<Settings>() };

                Assert.True(await Run(overload, graph, results, new Dictionary<object, object> { ["max"] = 3 }));
                Assert.Empty(results);
            }

            /// <summary>
            /// The ValidationContext constructor copies the items into a new dictionary. An
            /// attribute that writes to ValidationContext.Items changes only that copy, so the
            /// caller's dictionary stays as it was.
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.-ctor
            /// </summary>
            [Fact]
            public void Attributes_that_write_items_do_not_change_the_callers_dictionary()
            {
                var items = new Dictionary<object, object> { ["seen"] = 0 };
                var graph = new Counted { Child = new Counted(), Items = new List<Counted> { new Counted() } };

                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(graph, new List<ValidationResult>(), items);

                Assert.True(valid);
                Assert.Equal(new Dictionary<object, object> { ["seen"] = 0 }, items);
            }

            public class CountItemsAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    validationContext.Items["seen"] = (int)validationContext.Items["seen"] + 1;
                    return ValidationResult.Success;
                }
            }

            public class Counted
            {
                [CountItems]
                public string Tag { get; set; }

                public Counted Child { get; set; }

                public List<Counted> Items { get; set; }
            }

            /// <summary>
            /// The validator validates the object you pass, not ValidationContext.ObjectInstance.
            /// See Note 1 in RecursiveDataAnnotationValidator.cs for why.
            /// </summary>
            [Fact]
            public void The_object_passed_is_validated_not_the_context_object_instance()
            {
                var passed = new Leaf { Name = null };
                var other = new Leaf { Name = "n" };

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursive(passed, new ValidationContext(other), results);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Name | The Name field is required."), ResultText.Describe(results));
            }

            /// <summary>
            /// The validator keeps no state between calls, so one instance gives the same results
            /// each time. An object seen in the first call is validated again in the second.
            /// </summary>
            [Fact]
            public void One_validator_gives_the_same_results_on_every_call()
            {
                var validator = new RecursiveDataAnnotationValidator();
                var graph = InvalidGraph();

                var first = new List<ValidationResult>();
                var second = new List<ValidationResult>();
                validator.TryValidateObjectRecursive(graph, first);
                validator.TryValidateObjectRecursive(graph, second);

                Assert.Equal(ResultText.Describe(first), ResultText.Describe(second));
                Assert.Equal(2, second.Count);
            }

            [Fact]
            public void Reusing_one_results_list_adds_the_results_again()
            {
                var validator = new RecursiveDataAnnotationValidator();
                var list = new List<ValidationResult>();

                validator.TryValidateObjectRecursive(new Leaf(), list);
                validator.TryValidateObjectRecursive(new Leaf(), list);

                Assert.Equal(2, list.Count);
            }
        }

        /// <summary>Objects reached through properties.</summary>
        public class NestedObjects
        {
            public class Parent
            {
                public Leaf Child { get; set; }

                [Required]
                public Leaf RequiredChild { get; set; }
            }

            [Fact]
            public void Null_nested_object_is_skipped()
            {
                Assert.True(Validate(new Parent { RequiredChild = new Leaf { Name = "n" } }, out var results));
                Assert.Empty(results);
            }

            /// <summary>[Required] on a property that holds an object is checked on the parent.</summary>
            [Fact]
            public void Required_nested_object_that_is_null_is_reported_on_the_parent()
            {
                Assert.False(Validate(new Parent(), out var results));
                Assert.Equal(ResultText.Expect("RequiredChild | The RequiredChild field is required."), results);
            }

            [Fact]
            public void Deep_chain_reports_the_full_dotted_path()
            {
                var root = new Node();
                var node = root;
                for (var i = 0; i < 5; i++)
                {
                    node.Name = "n";
                    node.Next = new Node();
                    node = node.Next;
                }

                Assert.False(Validate(root, out var results));
                Assert.Equal(ResultText.Expect("Next.Next.Next.Next.Next.Name | The Name field is required."), results);
            }

            /// <summary>
            /// A chain a hundred objects deep is validated without running out of stack.
            /// The old version recurses once per level, so very deep graphs (thousands of levels)
            /// can overflow the stack in it. Since 3.0 the walk uses a queue and stops at
            /// 128 levels, failing the validation (see MaxDepthTests), so a chain of 200, which
            /// this test used before, is no longer the same in both versions.
            /// </summary>
            [Fact]
            public void Chain_of_one_hundred_objects_is_validated()
            {
                var root = new Node { Name = "n" };
                var node = root;
                for (var i = 0; i < 100; i++)
                {
                    node.Next = new Node { Name = "n" };
                    node = node.Next;
                }
                node.Name = null;

                Assert.False(Validate(root, out var results));
                var only = Assert.Single(results);
                Assert.StartsWith(string.Concat(Enumerable.Repeat("Next.", 100)) + "Name |", only);
            }

            public class Node
            {
                [Required]
                public string Name { get; set; }

                public Node Next { get; set; }
            }

            public abstract class Shape
            {
                [Required]
                public string Label { get; set; }
            }

            public class Circle : Shape
            {
                [Range(1, 100)]
                public int Radius { get; set; }

                public Leaf Center { get; set; }
            }

            public interface IHasName
            {
                string Name { get; }
            }

            public class Named : IHasName
            {
                [Required]
                public string Name { get; set; }

                public Leaf Extra { get; set; }
            }

            public class Drawing
            {
                public Shape Shape { get; set; }

                public IHasName Named { get; set; }

                public object Anything { get; set; }
            }

            /// <summary>
            /// The validator reads a property's value and walks it by its runtime type, not by the
            /// declared type. So the attributes and properties of a derived class are checked even
            /// when the property is declared as the base class, an interface or object.
            /// See: https://learn.microsoft.com/dotnet/api/system.object.gettype
            /// </summary>
            [Fact]
            public void Property_declared_as_a_base_class_is_validated_by_the_runtime_type()
            {
                var drawing = new Drawing { Shape = new Circle { Label = null, Radius = 0, Center = new Leaf() } };

                Assert.False(Validate(drawing, out var results));
                Assert.Equal(ResultText.Expect(
                    "Shape.Label | The Label field is required.",
                    "Shape.Radius | The field Radius must be between 1 and 100.",
                    "Shape.Center.Name | The Name field is required."),
                    results);
            }

            [Fact]
            public void Property_declared_as_an_interface_is_validated_by_the_runtime_type()
            {
                var drawing = new Drawing { Named = new Named { Name = "n", Extra = new Leaf() } };

                Assert.False(Validate(drawing, out var results));
                Assert.Equal(ResultText.Expect("Named.Extra.Name | The Name field is required."), results);
            }

            [Fact]
            public void Property_declared_as_object_is_validated_by_the_runtime_type()
            {
                var drawing = new Drawing { Anything = new Leaf() };

                Assert.False(Validate(drawing, out var results));
                Assert.Equal(ResultText.Expect("Anything.Name | The Name field is required."), results);
            }

            /// <summary>
            /// An int, string or DateTime held in an object property has nothing to validate. In
            /// 2.2 a string was enumerated as characters, and in 2.3 it is skipped. Either way the
            /// answer is the same.
            /// </summary>
            [Theory]
            [MemberData(nameof(SimpleValues))]
            public void Property_declared_as_object_holding_a_simple_value_is_valid(object value)
            {
                Assert.True(Validate(new Drawing { Anything = value }, out var results));
                Assert.Empty(results);
            }

            public static IEnumerable<object[]> SimpleValues() => new[]
            {
                new object[] { 42 },
                new object[] { "text" },
                new object[] { string.Empty },
                new object[] { new DateTime(2026, 10, 1) },
                new object[] { Guid.Empty },
                new object[] { 1.5m },
                new object[] { DayOfWeek.Friday },
                new object[] { new byte[] { 1, 2, 3 } },
            };

            public class Wrapper<T>
            {
                public T Value { get; set; }
            }

            public class WrapperHolder
            {
                public Wrapper<Leaf> Wrapped { get; set; }

                public Wrapper<Wrapper<Leaf>> DoubleWrapped { get; set; }
            }

            [Fact]
            public void Generic_wrapper_reports_the_path_through_its_property()
            {
                var holder = new WrapperHolder
                {
                    Wrapped = new Wrapper<Leaf> { Value = new Leaf() },
                    DoubleWrapped = new Wrapper<Wrapper<Leaf>> { Value = new Wrapper<Leaf> { Value = new Leaf() } },
                };

                Assert.False(Validate(holder, out var results));
                Assert.Equal(ResultText.Expect(
                    "Wrapped.Value.Name | The Name field is required.",
                    "DoubleWrapped.Value.Value.Name | The Name field is required."),
                    results);
            }

            public class Base
            {
                [Required]
                public string BaseName { get; set; }

                public Leaf BaseChild { get; set; }
            }

            public class Derived : Base
            {
                [Required]
                public string DerivedName { get; set; }
            }

            public class InheritanceHolder
            {
                public Derived Item { get; set; }
            }

            [Fact]
            public void Inherited_attributes_and_properties_are_validated()
            {
                var holder = new InheritanceHolder { Item = new Derived { BaseChild = new Leaf() } };

                Assert.False(Validate(holder, out var results));
                Assert.Equal(ResultText.Expect(
                    "Item.BaseName | The BaseName field is required.",
                    "Item.DerivedName | The DerivedName field is required.",
                    "Item.BaseChild.Name | The Name field is required."),
                    results);
            }

            /// <summary>
            /// A record's positional parameter makes a property. `[property: Required]` puts the
            /// attribute on that property, where Validator looks for it.
            /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record#positional-syntax-for-property-definition
            /// </summary>
            public record Tag([property: Required] string Name, Leaf Owner = null);

            public class Tagged
            {
                public Tag Main { get; set; }

                public List<Tag> Tags { get; set; }
            }

            [Fact]
            public void Records_with_different_values_are_each_validated()
            {
                var tagged = new Tagged
                {
                    Main = new Tag("main", new Leaf()),
                    Tags = new List<Tag> { new Tag("a"), new Tag(null), new Tag("c", new Leaf { Name = "o" }) },
                };

                Assert.False(Validate(tagged, out var results));
                Assert.Equal(ResultText.Expect(
                    "Main.Owner.Name | The Name field is required.",
                    "Tags[1].Name | The Name field is required."),
                    results);
            }

            /// <summary>
            /// An entity whose Equals compares only an Id. While no two objects in the graph share
            /// an Id, each one is validated in both versions.
            /// </summary>
            public class Entity
            {
                public int Id { get; set; }

                [Required]
                public string Name { get; set; }

                public Entity Parent { get; set; }

                public List<Entity> Children { get; set; }

                public override bool Equals(object obj) => obj is Entity other && other.Id == Id;

                public override int GetHashCode() => Id;
            }

            [Fact]
            public void Entities_with_different_ids_are_each_validated()
            {
                var root = new Entity
                {
                    Id = 1,
                    Name = "root",
                    Parent = new Entity { Id = 2, Name = null },
                    Children = new List<Entity>
                    {
                        new Entity { Id = 3, Name = "c" },
                        new Entity { Id = 4, Name = null },
                    },
                };

                Assert.False(Validate(root, out var results));
                Assert.Equal(ResultText.Expect(
                    "Parent.Name | The Name field is required.",
                    "Children[1].Name | The Name field is required."),
                    results);
            }

            public class Shapes
            {
                [Required]
                public string Title { get; set; }

                // a get-only computed property is read and walked like any other
                public Leaf Computed => new Leaf { Name = Title == "bad" ? null : "n" };

                // init-only and private-setter properties are public to read, so they are walked
                public Leaf InitOnly { get; init; }

                public Leaf PrivateSet { get; private set; }

                // not public, so reflection's GetProperties() does not return it
                private Leaf Hidden { get; } = new Leaf();

                internal Leaf Internal { get; set; } = new Leaf();

                // no getter, so there is nothing to read
                public Leaf WriteOnly { set { } }

                // an indexer takes arguments, so it cannot be read like a property
                public Leaf this[int index] => new Leaf();

                public void SetPrivate(Leaf leaf) => PrivateSet = leaf;

                public Leaf PeekHidden() => Hidden;
            }

            [Fact]
            public void Computed_property_is_walked()
            {
                Assert.False(Validate(new Shapes { Title = "bad" }, out var results));
                Assert.Equal(ResultText.Expect("Computed.Name | The Name field is required."), results);
            }

            [Fact]
            public void Init_only_and_private_setter_properties_are_walked()
            {
                var shapes = new Shapes { Title = "t", InitOnly = new Leaf() };
                shapes.SetPrivate(new Leaf());

                Assert.False(Validate(shapes, out var results));
                Assert.Equal(ResultText.Expect(
                    "InitOnly.Name | The Name field is required.",
                    "PrivateSet.Name | The Name field is required."),
                    results);
            }

            /// <summary>
            /// Only public instance and static properties are walked. Non-public properties,
            /// write-only properties and indexers are not.
            /// See: https://learn.microsoft.com/dotnet/api/system.type.getproperties
            /// </summary>
            [Fact]
            public void Non_public_write_only_and_indexer_properties_are_not_walked()
            {
                var shapes = new Shapes { Title = "t" };

                Assert.Null(shapes.PeekHidden().Name);
                Assert.True(Validate(shapes, out var results));
                Assert.Empty(results);
            }
        }

        /// <summary>Collection properties and their items.</summary>
        public class Collections
        {
            private static Leaf Bad() => new Leaf();

            private static Leaf Good() => new Leaf { Name = "n" };

            public class Holder
            {
                public object Items { get; set; }
            }

            /// <summary>
            /// Each collection type is walked through IEnumerable, so the index is the position in
            /// enumeration order. The invalid item is second in each case below.
            /// See: https://learn.microsoft.com/dotnet/api/system.collections.ienumerable
            /// </summary>
            [Theory]
            [MemberData(nameof(CollectionsWithTheBadItemSecond))]
            public void Item_index_is_the_position_in_enumeration_order(string kind, object items)
            {
                Assert.False(Validate(new Holder { Items = items }, out var results), kind);
                Assert.Equal(ResultText.Expect("Items[1].Name | The Name field is required."), results);
            }

            public static IEnumerable<object[]> CollectionsWithTheBadItemSecond()
            {
                yield return new object[] { "array", new[] { Good(), Bad(), Good() } };
                yield return new object[] { "List", new List<Leaf> { Good(), Bad(), Good() } };
                yield return new object[] { "Collection", new Collection<Leaf> { Good(), Bad() } };
                yield return new object[] { "ObservableCollection", new ObservableCollection<Leaf> { Good(), Bad() } };
                yield return new object[] { "ReadOnlyCollection", new List<Leaf> { Good(), Bad() }.AsReadOnly() };
                yield return new object[] { "LinkedList", new LinkedList<Leaf>(new[] { Good(), Bad() }) };
                yield return new object[] { "Queue", new Queue<Leaf>(new[] { Good(), Bad() }) };
                yield return new object[] { "HashSet", new HashSet<Leaf> { Good(), Bad() } };
                yield return new object[] { "ArrayList", new ArrayList { Good(), Bad() } };
                yield return new object[] { "object[] of mixed items", new object[] { 7, Bad(), "text" } };
                yield return new object[] { "List<object>", new List<object> { "text", Bad() } };
                yield return new object[] { "LINQ query", new[] { Good(), Bad() }.Select(x => x) };
                yield return new object[] { "iterator", Yield(Good(), Bad()) };
            }

            private static IEnumerable<Leaf> Yield(params Leaf[] items)
            {
                foreach (var item in items) yield return item;
            }

            /// <summary>
            /// A Stack enumerates from the top, so the item pushed last is index 0.
            /// See: https://learn.microsoft.com/dotnet/api/system.collections.generic.stack-1
            /// </summary>
            [Fact]
            public void Stack_index_counts_from_the_top()
            {
                var stack = new Stack<Leaf>();
                stack.Push(Bad());
                stack.Push(Good());
                stack.Push(Good());

                Assert.False(Validate(new Holder { Items = stack }, out var results));
                Assert.Equal(ResultText.Expect("Items[2].Name | The Name field is required."), results);
            }

            /// <summary>
            /// A dictionary is a collection of KeyValuePair items, so a value is reached through
            /// ".Value", and the index is the position in enumeration order, not the key. A
            /// SortedDictionary enumerates in key order.
            /// See: https://learn.microsoft.com/dotnet/api/system.collections.generic.keyvaluepair-2
            /// See: https://learn.microsoft.com/dotnet/api/system.collections.generic.sorteddictionary-2
            /// </summary>
            [Fact]
            public void Dictionary_value_is_reached_through_Value()
            {
                var map = new SortedDictionary<string, Leaf> { ["b"] = Bad(), ["a"] = Good(), ["c"] = Good() };

                Assert.False(Validate(new Holder { Items = map }, out var results));
                Assert.Equal(ResultText.Expect("Items[1].Value.Name | The Name field is required."), results);
            }

            [Fact]
            public void Tuple_items_are_reached_through_their_properties()
            {
                var tuple = Tuple.Create(Good(), 5, Bad());

                Assert.False(Validate(new Holder { Items = tuple }, out var results));
                Assert.Equal(ResultText.Expect("Items.Item3.Name | The Name field is required."), results);
            }

            [Fact]
            public void Null_items_are_skipped_but_keep_their_index()
            {
                Assert.False(Validate(new Holder { Items = new List<Leaf> { null, null, Bad() } }, out var results));
                Assert.Equal(ResultText.Expect("Items[2].Name | The Name field is required."), results);
            }

            [Fact]
            public void Every_invalid_item_is_reported()
            {
                Assert.False(Validate(new Holder { Items = new[] { Bad(), Good(), Bad(), Bad() } }, out var results));
                Assert.Equal(ResultText.Expect(
                    "Items[0].Name | The Name field is required.",
                    "Items[2].Name | The Name field is required.",
                    "Items[3].Name | The Name field is required."),
                    results);
            }

            [Fact]
            public void Items_are_walked_into()
            {
                var items = new List<NestedObjects.Node>
                {
                    new NestedObjects.Node { Name = "a", Next = new NestedObjects.Node { Name = null } },
                };

                Assert.False(Validate(new Holder { Items = items }, out var results));
                Assert.Equal(ResultText.Expect("Items[0].Next.Name | The Name field is required."), results);
            }

            [Fact]
            public void Collections_of_collection_holders_report_both_indexes()
            {
                var outer = new List<Holder>
                {
                    new Holder(),
                    new Holder { Items = new List<Leaf> { Good(), Bad() } },
                };

                Assert.False(Validate(new Holder { Items = outer }, out var results));
                Assert.Equal(ResultText.Expect("Items[1].Items[1].Name | The Name field is required."), results);
            }

            public class Rules
            {
                [Required]
                public List<Leaf> RequiredList { get; set; }

                [MinLength(1)]
                public Leaf[] AtLeastOne { get; set; }

                [MaxLength(2)]
                public Leaf[] AtMostTwo { get; set; }
            }

            [Fact]
            public void Empty_collections_pass_unless_an_attribute_says_otherwise()
            {
                var rules = new Rules { RequiredList = new List<Leaf>(), AtLeastOne = new Leaf[0], AtMostTwo = new Leaf[0] };

                Assert.False(Validate(rules, out var results));
                Assert.Equal(new[] { "AtLeastOne" }, results.Select(r => r.Split(' ')[0]));
            }

            [Fact]
            public void Attributes_on_a_collection_property_run_on_the_parent()
            {
                var rules = new Rules { RequiredList = null, AtLeastOne = new[] { Good() }, AtMostTwo = new[] { Good(), Good(), Bad() } };

                Assert.False(Validate(rules, out var results));
                Assert.Equal(new[] { "AtMostTwo", "AtMostTwo[2].Name", "RequiredList" }, results.Select(r => r.Split(' ')[0]));
            }

            [Flags]
            public enum Color
            {
                Red = 1,
                Green = 2,
            }

            public struct Point
            {
                public int X { get; set; }
                public int Y { get; set; }
            }

            public class Primitives
            {
                public int[] Ints { get; set; }
                public byte[] Bytes { get; set; }
                public List<string> Strings { get; set; }
                public List<DateTime> Dates { get; set; }
                public List<Guid> Ids { get; set; }
                public List<decimal?> Amounts { get; set; }
                public HashSet<Color> Colors { get; set; }
                public Dictionary<string, string> Headers { get; set; }
                public Dictionary<int, List<string>> Groups { get; set; }
                public List<Point> Points { get; set; }
                public IEnumerable<char> Letters { get; set; }
            }

            /// <summary>
            /// Collections of types with nothing to validate always pass, whatever they hold,
            /// including null and empty strings. 2.2 enumerated them and found nothing. 2.3 skips
            /// most of them without enumerating, and finds nothing in the rest.
            /// </summary>
            [Fact]
            public void Collections_of_types_with_nothing_to_validate_pass()
            {
                var primitives = new Primitives
                {
                    Ints = new[] { -1, 0, int.MaxValue },
                    Bytes = new byte[1024],
                    Strings = new List<string> { null, "", " " },
                    Dates = new List<DateTime> { DateTime.MinValue },
                    Ids = new List<Guid> { Guid.Empty },
                    Amounts = new List<decimal?> { null, -1m },
                    Colors = new HashSet<Color> { (Color)99 },
                    Headers = new Dictionary<string, string> { ["k"] = null },
                    Groups = new Dictionary<int, List<string>> { [1] = new List<string> { null } },
                    Points = new List<Point> { new Point { X = -1 } },
                    Letters = "abc",
                };

                Assert.True(Validate(primitives, out var results));
                Assert.Empty(results);
            }

            /// <summary>
            /// A struct in a collection is boxed and validated like any object, so its property
            /// attributes run. A struct that is not a collection, held directly in a property, is not walked.
            /// See: https://learn.microsoft.com/dotnet/csharp/programming-guide/types/boxing-and-unboxing
            /// </summary>
            public struct CheckedPoint
            {
                [Range(0, 10)]
                public int X { get; set; }
            }

            public class CheckedPoints
            {
                public List<CheckedPoint> Points { get; set; }

                public CheckedPoint Single { get; set; }
            }

            [Fact]
            public void Struct_items_with_attributes_are_validated()
            {
                var points = new CheckedPoints
                {
                    Points = new List<CheckedPoint> { new CheckedPoint { X = 1 }, new CheckedPoint { X = 11 } },
                    Single = new CheckedPoint { X = 99 },
                };

                Assert.False(Validate(points, out var results));
                Assert.Equal(ResultText.Expect("Points[1].X | The field X must be between 0 and 10."), results);
            }

            /// <summary>
            /// A query is enumerated each time it is read. The validator reads the property once
            /// and enumerates the query once per validation.
            /// See: https://learn.microsoft.com/dotnet/standard/linq/deferred-execution-lazy-evaluation
            /// </summary>
            [Fact]
            public void Query_of_objects_runs_once_per_validation()
            {
                var runs = 0;
                var source = new[] { Good(), Bad() };
                var query = source.Select(x =>
                {
                    runs++;
                    return x;
                });

                Assert.False(Validate(new Holder { Items = query }, out var results));
                Assert.Equal(2, runs);
                Assert.Equal(ResultText.Expect("Items[1].Name | The Name field is required."), results);
            }
        }

        /// <summary>The [SkipRecursiveValidation] attribute.</summary>
        public class SkipAttribute
        {
            public class Parent
            {
                [SkipRecursiveValidation]
                public Leaf Skipped { get; set; }

                [SkipRecursiveValidation]
                [Required]
                public Leaf SkippedButRequired { get; set; }

                [SkipRecursiveValidation]
                public List<Leaf> SkippedList { get; set; }

                public Leaf Walked { get; set; }
            }

            public class Child : Parent
            {
            }

            [Fact]
            public void Skipped_object_and_collection_are_not_walked()
            {
                var parent = new Parent
                {
                    Skipped = new Leaf(),
                    SkippedButRequired = new Leaf(),
                    SkippedList = new List<Leaf> { new Leaf() },
                    Walked = new Leaf { Name = "n" },
                };

                Assert.True(Validate(parent, out var results));
                Assert.Empty(results);
            }

            [Fact]
            public void Attributes_on_a_skipped_property_still_run_on_the_parent()
            {
                Assert.False(Validate(new Parent(), out var results));
                Assert.Equal(ResultText.Expect("SkippedButRequired | The SkippedButRequired field is required."), results);
            }

            [Fact]
            public void Skip_on_an_inherited_property_applies_to_the_derived_class()
            {
                var child = new Child { SkippedButRequired = new Leaf(), Skipped = new Leaf(), Walked = new Leaf() };

                Assert.False(Validate(child, out var results));
                Assert.Equal(ResultText.Expect("Walked.Name | The Name field is required."), results);
            }

            [Fact]
            public void Skip_applies_only_where_the_object_is_reached_through_a_skipped_property()
            {
                var shared = new Leaf();
                var parent = new Parent { Skipped = shared, SkippedButRequired = new Leaf(), Walked = shared };

                Assert.False(Validate(parent, out var results));
                Assert.Equal(ResultText.Expect("Walked.Name | The Name field is required."), results);
            }
        }

        /// <summary>
        /// The validator tracks each object it has visited, so a graph with cycles ends, and an
        /// object reached twice is validated once, under the first path the walk reaches.
        /// </summary>
        public class CyclesAndSharedObjects
        {
            public class Person
            {
                [Required]
                public string Name { get; set; }

                public Person Partner { get; set; }

                public Person Self { get; set; }

                public List<Person> Friends { get; set; }

                public Leaf Badge { get; set; }

                public Leaf SpareBadge { get; set; }
            }

            [Fact]
            public void Two_objects_that_reference_each_other_end()
            {
                var a = new Person { Name = "a" };
                var b = new Person { Name = null, Partner = a };
                a.Partner = b;

                Assert.False(Validate(a, out var results));
                Assert.Equal(ResultText.Expect("Partner.Name | The Name field is required."), results);
            }

            [Fact]
            public void Object_that_references_itself_ends()
            {
                var a = new Person { Name = null };
                a.Self = a;

                Assert.False(Validate(a, out var results));
                Assert.Equal(ResultText.Expect("Name | The Name field is required."), results);
            }

            [Fact]
            public void Cycle_through_a_list_ends()
            {
                var a = new Person { Name = "a" };
                var b = new Person { Name = null, Friends = new List<Person> { a } };
                a.Friends = new List<Person> { b, a };

                Assert.False(Validate(a, out var results));
                Assert.Equal(ResultText.Expect("Friends[0].Name | The Name field is required."), results);
            }

            [Fact]
            public void Shared_object_is_reported_under_the_first_property_only()
            {
                var badge = new Leaf();
                var person = new Person { Name = "p", Badge = badge, SpareBadge = badge };

                Assert.False(Validate(person, out var results));
                Assert.Equal(ResultText.Expect("Badge.Name | The Name field is required."), results);
            }

            [Fact]
            public void Same_item_twice_in_a_list_is_reported_at_the_first_index_only()
            {
                var bad = new Person { Name = null };
                var person = new Person { Name = "p", Friends = new List<Person> { new Person { Name = "x" }, bad, bad } };

                Assert.False(Validate(person, out var results));
                Assert.Equal(ResultText.Expect("Friends[1].Name | The Name field is required."), results);
            }

            [Fact]
            public void Object_reached_through_a_property_and_a_list_is_reported_once()
            {
                var bad = new Person { Name = null };
                var person = new Person { Name = "p", Partner = bad, Friends = new List<Person> { bad } };

                Assert.False(Validate(person, out var results));
                Assert.Equal(ResultText.Expect("Partner.Name | The Name field is required."), results);
            }

            [Fact]
            public void Shared_object_is_validated_again_in_a_separate_call()
            {
                var badge = new Leaf();
                var validator = new RecursiveDataAnnotationValidator();

                var first = new List<ValidationResult>();
                var second = new List<ValidationResult>();
                validator.TryValidateObjectRecursive(new Person { Name = "a", Badge = badge }, first);
                validator.TryValidateObjectRecursive(new Person { Name = "b", Badge = badge }, second);

                Assert.Single(first);
                Assert.Single(second);
            }
        }

        /// <summary>Attributes that read the ValidationContext of the object they are on.</summary>
        public class ContextAwareAttributes
        {
            /// <summary>
            /// A cross-field check: it reads another property through ObjectInstance. Each nested
            /// object gets its own ValidationContext, so ObjectInstance is the nested object,
            /// not the root.
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.objectinstance
            /// </summary>
            public class MatchesAttribute : ValidationAttribute
            {
                private readonly string _other;

                public MatchesAttribute(string other) => _other = other;

                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    var otherValue = validationContext.ObjectType.GetProperty(_other).GetValue(validationContext.ObjectInstance);
                    return Equals(value, otherValue)
                        ? ValidationResult.Success
                        : new ValidationResult($"{validationContext.MemberName} must match {_other}.", new[] { validationContext.MemberName });
                }
            }

            public class Account
            {
                public string Password { get; set; }

                [Matches(nameof(Password))]
                public string Confirm { get; set; }

                [CustomValidation(typeof(Account), nameof(CheckEven))]
                public int Even { get; set; }

                public static ValidationResult CheckEven(int value, ValidationContext context) =>
                    value % 2 == 0
                        ? ValidationResult.Success
                        : new ValidationResult($"{context.MemberName} must be even.", new[] { context.MemberName });
            }

            public class Signup
            {
                public string Password { get; set; } = "root";

                public Account Account { get; set; }

                public List<Account> Others { get; set; }
            }

            [Fact]
            public void Cross_field_attribute_reads_the_nested_object()
            {
                var signup = new Signup
                {
                    Account = new Account { Password = "a", Confirm = "root" },
                    Others = new List<Account> { new Account { Password = "x", Confirm = "x" }, new Account { Password = "y", Confirm = "z" } },
                };

                Assert.False(Validate(signup, out var results));
                Assert.Equal(ResultText.Expect(
                    "Account.Confirm | Confirm must match Password.",
                    "Others[1].Confirm | Confirm must match Password."),
                    results);
            }

            /// <summary>
            /// CustomValidationAttribute calls a static method you name.
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.customvalidationattribute
            /// </summary>
            [Fact]
            public void Custom_validation_method_runs_on_nested_objects()
            {
                var signup = new Signup { Account = new Account { Even = 3 }, Others = new List<Account> { new Account { Even = 2 } } };

                Assert.False(Validate(signup, out var results));
                Assert.Equal(ResultText.Expect("Account.Even | Even must be even."), results);
            }

            /// <summary>
            /// Each object gets a new ValidationContext with the object's own type, so an
            /// attribute sees the type of the object it is on.
            /// </summary>
            [Fact]
            public void Each_object_gets_a_context_for_its_own_type()
            {
                var seen = new List<Type>();
                var holder = new TypeRecorderHolder
                {
                    Recorder = new TypeRecorder(),
                    Recorders = new List<TypeRecorder> { new TypeRecorder() },
                };

                var valid = new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursive(holder, new List<ValidationResult>(), new Dictionary<object, object> { ["seen"] = seen });

                Assert.True(valid);
                Assert.Equal(new[] { typeof(TypeRecorderHolder), typeof(TypeRecorder), typeof(TypeRecorder) }, seen);
            }

            public class RecordTypeAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    ((List<Type>)validationContext.Items["seen"]).Add(validationContext.ObjectType);
                    return ValidationResult.Success;
                }
            }

            public class TypeRecorder
            {
                [RecordType]
                public string Tag { get; set; }
            }

            public class TypeRecorderHolder
            {
                [RecordType]
                public string Tag { get; set; }

                public TypeRecorder Recorder { get; set; }

                public List<TypeRecorder> Recorders { get; set; }
            }
        }

        /// <summary>The async methods run the same validation as the sync ones.</summary>
        public class AsyncParity
        {
            private static Results.Customer Invalid() => new Results.Customer
            {
                Name = null,
                Rank = 11,
                Address = new Results.Address { Zip = null, City = "c" },
            };

            [Fact]
            public async Task Async_with_context_matches_sync()
            {
                var validator = new RecursiveDataAnnotationValidator();
                var sync = new List<ValidationResult>();
                var async = new List<ValidationResult>();
                var model = Invalid();

                var syncValid = validator.TryValidateObjectRecursive(model, new ValidationContext(model), sync);
                var asyncValid = await validator.TryValidateObjectRecursiveAsync(model, new ValidationContext(model), async);

                Assert.Equal(syncValid, asyncValid);
                Assert.Equal(ResultText.Describe(sync), ResultText.Describe(async));
                Assert.Equal(4, async.Count);
            }

            [Fact]
            public async Task Async_with_items_matches_sync()
            {
                var validator = new RecursiveDataAnnotationValidator();
                var sync = new List<ValidationResult>();
                var async = new List<ValidationResult>();
                var model = Invalid();

                var syncValid = validator.TryValidateObjectRecursive(model, sync);
                var asyncValid = await validator.TryValidateObjectRecursiveAsync(model, async);

                Assert.Equal(syncValid, asyncValid);
                Assert.Equal(ResultText.Describe(sync), ResultText.Describe(async));
                Assert.Equal(4, async.Count);
            }

            [Fact]
            public async Task Interfaces_give_the_same_results_as_the_class()
            {
                IRecursiveDataAnnotationValidator sync = new RecursiveDataAnnotationValidator();
                IAsyncRecursiveDataAnnotationValidator async = new RecursiveDataAnnotationValidator();
                var syncResults = new List<ValidationResult>();
                var asyncResults = new List<ValidationResult>();

                Assert.False(sync.TryValidateObjectRecursive(Invalid(), syncResults));
                Assert.False(await async.TryValidateObjectRecursiveAsync(Invalid(), asyncResults));
                Assert.Equal(ResultText.Describe(syncResults), ResultText.Describe(asyncResults));
            }
        }
    }
}
