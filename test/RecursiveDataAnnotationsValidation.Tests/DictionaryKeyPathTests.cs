using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// A value of a dictionary is reported by its key: Map[Primary].Name. Before 3.0 it was
    /// reported by its position in enumeration order, through the KeyValuePair that the dictionary
    /// yields: Map[0].Value.Name.
    ///
    /// The form is the one that MVC model binding uses for a dictionary, such as
    /// selectedCourses[1050]=Chemistry. The key is not quoted and not escaped, so a key that
    /// contains "]" or "." makes the path ambiguous.
    /// See: https://learn.microsoft.com/aspnet/core/mvc/models/model-binding
    ///
    /// The rules:
    /// - A dictionary implements IDictionary, IDictionary&lt;TKey, TValue&gt; or
    ///   IReadOnlyDictionary&lt;TKey, TValue&gt;. A list of KeyValuePair items is not one.
    /// - A key names its value when it is a string, a primitive, an enum, or a struct that formats
    ///   itself, such as a decimal or a Guid, and it has nothing to validate. The text uses the
    ///   invariant culture, so a decimal key is "1.5" on a German server too.
    ///   See: https://learn.microsoft.com/dotnet/api/system.globalization.cultureinfo.invariantculture
    /// - Any other key, such as an object of your own, keeps the old form for that entry, so the
    ///   key is still validated: Map[0].Key.Name and Map[0].Value.Name.
    /// - The value is one level below the dictionary, as an item of a list is. Before, the
    ///   KeyValuePair was one more level, so a value now comes earlier in the results.
    /// </summary>
    public class DictionaryKeyPathTests
    {
        public class Leaf
        {
            [Required]
            public string Name { get; set; }
        }

        public struct Line
        {
            [Required]
            public string Text { get; set; }
        }

        public class Holder<T>
        {
            public T Map { get; set; }
        }

        // An attribute that never fails. On an enum it makes the enum a type with something to
        // validate, so it is not a leaf type and cannot name a value.
        [AttributeUsage(AttributeTargets.Enum)]
        public class AlwaysValidAttribute : ValidationAttribute
        {
            public override bool IsValid(object value) => true;
        }

        [AlwaysValid]
        public enum CheckedColor { Red }

        // A dictionary that implements only IReadOnlyDictionary.
        public class ReadOnlyLeafMap : IReadOnlyDictionary<string, Leaf>
        {
            private readonly Dictionary<string, Leaf> _inner;

            public ReadOnlyLeafMap(Dictionary<string, Leaf> inner)
            {
                _inner = inner;
            }

            public Leaf this[string key] => _inner[key];
            public IEnumerable<string> Keys => _inner.Keys;
            public IEnumerable<Leaf> Values => _inner.Values;
            public int Count => _inner.Count;
            public bool ContainsKey(string key) => _inner.ContainsKey(key);
            public bool TryGetValue(string key, out Leaf value) => _inner.TryGetValue(key, out value);
            public IEnumerator<KeyValuePair<string, Leaf>> GetEnumerator() => _inner.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public class Ordered
        {
            public Leaf First { get; set; }
            public Dictionary<string, Leaf> Map { get; set; }
            public List<Leaf> Items { get; set; }
        }

        public class TwoMaps<T>
        {
            public Dictionary<string, T> First { get; set; }
            public Dictionary<string, T> Second { get; set; }
        }

        // Adds an invalid entry to the dictionary that holds it, when it is validated.
        public class Adder : IValidatableObject
        {
            public string Name { get; set; } = "ok";

            public Dictionary<string, Leaf> Owner { get; set; }

            public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
            {
                if (Owner != null && !Owner.ContainsKey("added"))
                {
                    Owner["added"] = new Leaf();
                }

                return Enumerable.Empty<ValidationResult>();
            }
        }

        public class AdderRoot
        {
            public Dictionary<string, Leaf> Map { get; set; }
            public Adder Adder { get; set; }
        }

        private const string NameRequired = " | The Name field is required.";
        private const string TextRequired = " | The Text field is required.";

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        // Spec.
        [Fact]
        public void String_key_names_the_value()
        {
            var (valid, errors) = Run(new Holder<Dictionary<string, Leaf>>
            {
                Map = new Dictionary<string, Leaf> { ["Primary"] = new Leaf() },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[Primary].Name" + NameRequired), errors);
        }

        // Spec. The key, not the position: a SortedDictionary enumerates in key order, so "b"
        // was Map[1] before.
        // See: https://learn.microsoft.com/dotnet/api/system.collections.generic.sorteddictionary-2
        [Fact]
        public void Key_is_used_and_not_the_position()
        {
            var (valid, errors) = Run(new Holder<SortedDictionary<string, Leaf>>
            {
                Map = new SortedDictionary<string, Leaf>
                {
                    ["b"] = new Leaf(),
                    ["a"] = new Leaf { Name = "ok" },
                    ["c"] = new Leaf { Name = "ok" },
                },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[b].Name" + NameRequired), errors);
        }

        // Spec.
        [Fact]
        public void Number_and_enum_keys_name_the_value()
        {
            Assert.Equal(
                ResultText.Expect("Map[42].Name" + NameRequired),
                Run(new Holder<Dictionary<int, Leaf>> { Map = new Dictionary<int, Leaf> { [42] = new Leaf() } }).Errors);

            Assert.Equal(
                ResultText.Expect("Map[Monday].Name" + NameRequired),
                Run(new Holder<Dictionary<DayOfWeek, Leaf>> { Map = new Dictionary<DayOfWeek, Leaf> { [DayOfWeek.Monday] = new Leaf() } }).Errors);
        }

        // Spec. The key is formatted with the invariant culture, so the path is the same on every
        // server. German formats 1.5 as "1,5".
        [Fact]
        public void Key_is_formatted_with_the_invariant_culture()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var (valid, errors) = Run(new Holder<Dictionary<decimal, Leaf>>
                {
                    Map = new Dictionary<decimal, Leaf> { [1.5m] = new Leaf() },
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Map[1.5].Name" + NameRequired), errors);
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        // Spec.
        [Fact]
        public void Guid_key_names_the_value()
        {
            var key = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

            var (valid, errors) = Run(new Holder<Dictionary<Guid, Leaf>> { Map = new Dictionary<Guid, Leaf> { [key] = new Leaf() } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[0f8fad5b-d9cb-469f-a165-70867728950e].Name" + NameRequired), errors);
        }

        // Spec. The key is not escaped, as in MVC, so this path cannot be told apart from a key
        // "b" that holds a list. The README says so.
        [Fact]
        public void Key_with_path_characters_is_not_escaped()
        {
            var (valid, errors) = Run(new Holder<Dictionary<string, Leaf>>
            {
                Map = new Dictionary<string, Leaf> { ["b.c[0]"] = new Leaf() },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[b.c[0]].Name" + NameRequired), errors);
        }

        // Guard. A key that is an object keeps the old form, so the key is validated too.
        [Fact]
        public void Object_key_keeps_the_index_form()
        {
            var (valid, errors) = Run(new Holder<Dictionary<Leaf, Leaf>>
            {
                Map = new Dictionary<Leaf, Leaf> { [new Leaf()] = new Leaf() },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[0].Key.Name" + NameRequired, "Map[0].Value.Name" + NameRequired), errors);
        }

        // Spec. The form is chosen for each entry. The second entry has an object key, so it uses
        // its position. A Dictionary with no removals enumerates in the order the entries were added.
        // See: https://learn.microsoft.com/dotnet/api/system.collections.generic.dictionary-2#remarks
        [Fact]
        public void Each_entry_chooses_its_own_form()
        {
            var (valid, errors) = Run(new Holder<Dictionary<object, Leaf>>
            {
                Map = new Dictionary<object, Leaf>
                {
                    ["a"] = new Leaf(),
                    [new Leaf { Name = "key" }] = new Leaf(),
                },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[a].Name" + NameRequired, "Map[1].Value.Name" + NameRequired), errors);
        }

        // Guard. An enum with a validation attribute has something to validate, so it does not
        // name the value, and the entry keeps the index form.
        [Fact]
        public void Key_with_something_to_validate_keeps_the_index_form()
        {
            var (valid, errors) = Run(new Holder<Dictionary<CheckedColor, Leaf>>
            {
                Map = new Dictionary<CheckedColor, Leaf> { [CheckedColor.Red] = new Leaf() },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[0].Value.Name" + NameRequired), errors);
        }

        // Spec.
        [Fact]
        public void Struct_value_is_named_by_its_key()
        {
            var (valid, errors) = Run(new Holder<Dictionary<string, Line>> { Map = new Dictionary<string, Line> { ["a"] = new Line() } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[a].Text" + TextRequired), errors);
        }

        // Guard.
        [Fact]
        public void Null_value_is_skipped()
        {
            var (valid, errors) = Run(new Holder<Dictionary<string, Leaf>> { Map = new Dictionary<string, Leaf> { ["a"] = null } });

            Assert.True(valid);
            Assert.Empty(errors);
        }

        // Guard. A list of KeyValuePair items is not a dictionary, so it keeps the index form.
        [Fact]
        public void List_of_pairs_keeps_the_index_form()
        {
            var (valid, errors) = Run(new Holder<List<KeyValuePair<string, Leaf>>>
            {
                Map = new List<KeyValuePair<string, Leaf>> { new KeyValuePair<string, Leaf>("a", new Leaf()) },
            });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[0].Value.Name" + NameRequired), errors);
        }

        public static IEnumerable<object[]> Dictionaries()
        {
            Func<Dictionary<string, Leaf>> one = () => new Dictionary<string, Leaf> { ["a"] = new Leaf() };

            yield return new object[] { "Hashtable", (Func<object>)(() => new Hashtable { ["a"] = new Leaf() }) };
            yield return new object[] { "SortedList", (Func<object>)(() => new SortedList<string, Leaf>(one())) };
            yield return new object[] { "ConcurrentDictionary", (Func<object>)(() => new ConcurrentDictionary<string, Leaf>(one())) };
            yield return new object[] { "ReadOnlyDictionary", (Func<object>)(() => new ReadOnlyDictionary<string, Leaf>(one())) };
            yield return new object[] { "IReadOnlyDictionary only", (Func<object>)(() => new ReadOnlyLeafMap(one())) };
#if NET8_0_OR_GREATER
            yield return new object[] { "ImmutableDictionary", (Func<object>)(() => System.Collections.Immutable.ImmutableDictionary.CreateRange(one())) };
#endif
        }

        // Spec. Every kind of dictionary uses the key.
        [Theory]
        [MemberData(nameof(Dictionaries))]
        public void Every_kind_of_dictionary_uses_the_key(string name, Func<object> create)
        {
            Assert.NotNull(name);

            var (valid, errors) = Run(new Holder<object> { Map = create() });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Map[a].Name" + NameRequired), errors);
        }

        // Spec. A value is one level below the dictionary, like an item of a list, so it comes in
        // property order with the items of Items. Before, it came after them: Map[0] was one more
        // level than Items[0].
        [Fact]
        public void Value_is_one_level_below_the_dictionary()
        {
            var (valid, errors) = Run(new Ordered
            {
                First = new Leaf(),
                Map = new Dictionary<string, Leaf> { ["a"] = new Leaf() },
                Items = new List<Leaf> { new Leaf() },
            });

            Assert.False(valid);
            Assert.Equal(
                ResultText.Expect("First.Name" + NameRequired, "Map[a].Name" + NameRequired, "Items[0].Name" + NameRequired),
                errors);
        }

        // Spec. A dictionary of structs that two properties hold reports each struct once, at the
        // shortest path, as an array of structs does.
        [Fact]
        public void Struct_value_in_a_dictionary_that_two_properties_hold_is_reported_once()
        {
            var shared = new Dictionary<string, Line> { ["a"] = new Line() };

            var (valid, errors) = Run(new TwoMaps<Line> { First = shared, Second = shared });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("First[a].Text" + TextRequired), errors);
        }

        // Spec. A second route enumerates the dictionary again and finds the entry that the
        // Validate method of Adder added in the meantime. Its value is a class object, so it is
        // not skipped as a struct would be.
        [Fact]
        public void Value_added_by_validation_is_found_by_a_second_route()
        {
            var map = new Dictionary<string, Leaf> { ["a"] = new Leaf { Name = "ok" } };

            var (valid, errors) = Run(new AdderRoot { Map = map, Adder = new Adder { Owner = map } });

            Assert.False(valid);
            Assert.Equal(ResultText.Expect("Adder.Owner[added].Name" + NameRequired), errors);
        }
    }
}
