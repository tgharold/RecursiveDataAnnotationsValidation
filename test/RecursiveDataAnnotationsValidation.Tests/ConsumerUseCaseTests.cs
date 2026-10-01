using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using RecursiveDataAnnotationsValidation.Tests.Attributes;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>Formats results as "member names | message" so a test can compare a whole result set.</summary>
    internal static class ResultText
    {
        public static List<string> Describe(IEnumerable<ValidationResult> results) =>
            results.Select(r => $"{string.Join(",", r.MemberNames)} | {r.ErrorMessage}")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();

        public static List<string> Expect(params string[] expected) =>
            expected.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Tests built from the ways that projects in the wild call the package. Each nested class
    /// says what that kind of caller does with the validator and why the shape matters.
    /// The tests only use the public API, so they also run against an older release of the library.
    /// The models and tests are written for this repository. They copy no code from any project.
    /// They follow only the shape of the data and the way the public API is called.
    /// </summary>
    public class ConsumerUseCaseTests
    {
        /// <summary>
        /// Use case: a web service that validates the JSON response of a third-party API with the
        /// overload that takes a ValidationContext.
        /// The caller only wants the true/false answer, so it passes null for the results list.
        /// The framework's Validator.TryValidateObject accepts a null list and still returns false
        /// for an invalid object. The recursive validator must do the same.
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject
        /// A failure on the root object works with a null list. A failure below the root fails,
        /// because the recursion adds the nested results to the list the caller passed in.
        /// The intended behavior is not decided: return false, or throw ArgumentNullException.
        /// </summary>
        public class NullResultsList
        {
            public class Leaf
            {
                [Required]
                public string Name { get; set; }
            }

            public class Parent
            {
                [Required]
                public string Title { get; set; }

                public Leaf Child { get; set; }

                public List<Leaf> Items { get; set; }
            }

            private static bool Validate(Parent parent) =>
                new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(
                    parent,
                    new ValidationContext(parent),
                    null);

            [Fact]
            public void Valid_object_returns_true()
            {
                var parent = new Parent { Title = "t", Child = new Leaf { Name = "n" }, Items = new List<Leaf> { new Leaf { Name = "n" } } };

                Assert.True(Validate(parent));
            }

            [Fact]
            public void Failure_on_the_root_object_returns_false()
            {
                var parent = new Parent { Title = null, Child = new Leaf { Name = "n" } };

                Assert.False(Validate(parent));
            }

            [Fact(Skip = "Not fixed yet. A failure in a nested object throws NullReferenceException when the results list is null.")]
            public void Failure_in_a_nested_object_returns_false()
            {
                var parent = new Parent { Title = "t", Child = new Leaf { Name = null } };

                var valid = true;
                var ex = Record.Exception(() => valid = Validate(parent));

                Assert.Null(ex);
                Assert.False(valid);
            }

            [Fact(Skip = "Not fixed yet. A failure in a collection item throws NullReferenceException when the results list is null.")]
            public void Failure_in_a_collection_item_returns_false()
            {
                var parent = new Parent { Title = "t", Items = new List<Leaf> { new Leaf { Name = "n" }, new Leaf { Name = null } } };

                var valid = true;
                var ex = Record.Exception(() => valid = Validate(parent));

                Assert.Null(ex);
                Assert.False(valid);
            }

            [Fact(Skip = "Not fixed yet. The overload without a ValidationContext throws NullReferenceException for a nested failure when the results list is null.")]
            public void Failure_in_a_nested_object_returns_false_without_a_validation_context()
            {
                var parent = new Parent { Title = "t", Child = new Leaf { Name = null } };

                var valid = true;
                var ex = Record.Exception(() =>
                    valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(parent, (List<ValidationResult>)null));

                Assert.Null(ex);
                Assert.False(valid);
            }

            [Fact(Skip = "Not fixed yet. The async overload throws NullReferenceException for a nested failure when the results list is null.")]
            public async Task Failure_in_a_nested_object_returns_false_async()
            {
                var parent = new Parent { Title = "t", Child = new Leaf { Name = null } };

                var valid = true;
                var ex = await Record.ExceptionAsync(async () =>
                    valid = await new RecursiveDataAnnotationValidator().TryValidateObjectRecursiveAsync(
                        parent,
                        new ValidationContext(parent),
                        null));

                Assert.Null(ex);
                Assert.False(valid);
            }
        }

        /// <summary>
        /// Use case: the model classes for a third-party JSON:API response.
        /// A base class holds `List&lt;Item&gt; Included`
        /// and a nullable `Dictionary&lt;string, string&gt; Links`. Derived classes add a required
        /// `Data` item. Callers validate the derived type, so the collection property is inherited.
        /// This guards three things:
        /// - Reflection returns inherited public properties, so the base list is walked.
        /// - A dictionary of strings, null or filled, never causes a failure of its own.
        ///   The leaf-type rule skips it without enumerating it. Results must match the release
        ///   before that rule, which enumerated every pair.
        /// - The member name starts with the property name only, with no type name: "Included[1].Id".
        /// See: https://learn.microsoft.com/dotnet/api/system.type.getproperties
        /// </summary>
        public class JsonApiShapedResponse
        {
            public class ObjectData
            {
                [Required]
                public string Id { get; set; }

                [Required]
                public string Type { get; set; }
            }

            public class ResponseBase
            {
                public List<ObjectData> Included { get; set; } = new List<ObjectData>();

                public Dictionary<string, string> Links { get; set; } = new Dictionary<string, string>();
            }

            public class ObjectResponse : ResponseBase
            {
                [Required]
                public ObjectData Data { get; set; }
            }

            private static (bool Valid, List<string> Errors) Run(ObjectResponse response)
            {
                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(
                    response,
                    new ValidationContext(response),
                    results);
                return (valid, ResultText.Describe(results));
            }

            [Fact]
            public void Valid_response_passes_whatever_the_links_hold()
            {
                foreach (var links in new Dictionary<string, string>[]
                         {
                             null,
                             new Dictionary<string, string>(),
                             new Dictionary<string, string> { ["self"] = "https://example.com/a", ["next"] = null },
                         })
                {
                    var response = new ObjectResponse
                    {
                        Data = new ObjectData { Id = "1", Type = "member" },
                        Included = new List<ObjectData> { new ObjectData { Id = "2", Type = "tier" } },
                        Links = links,
                    };

                    var (valid, errors) = Run(response);

                    Assert.True(valid);
                    Assert.Empty(errors);
                }
            }

            [Fact]
            public void Inherited_collection_property_reports_item_errors()
            {
                var response = new ObjectResponse
                {
                    Data = null,
                    Included = new List<ObjectData>
                    {
                        new ObjectData { Id = "1", Type = "tier" },
                        new ObjectData { Id = null, Type = "tier" },
                    },
                };

                var (valid, errors) = Run(response);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Data | The Data field is required.",
                        "Included[1].Id | The Id field is required."),
                    errors);
            }
        }

        /// <summary>
        /// Use case: an options validator that implements IValidateOptions&lt;T&gt;. Several projects
        /// write this small class. The options system registers it as a singleton, so one
        /// RecursiveDataAnnotationValidator in a readonly field serves every thread that reads an
        /// options value. The test shares one validator between many tasks and checks each result.
        /// The leaf-type caches are static and fill on first use, so the tasks start together
        /// to make them race on a cold cache. The model types here are used by no other test.
        /// A Task.Run per call uses the thread pool, so xUnit's own parallelism does not matter here.
        /// See: https://learn.microsoft.com/dotnet/api/microsoft.extensions.options.ivalidateoptions-1
        /// See: https://xunit.net/docs/running-tests-in-parallel
        /// </summary>
        public class SharedValidatorAcrossThreads
        {
            public class Order
            {
                [Required]
                public string Number { get; set; }

                public List<Line> Lines { get; set; } = new List<Line>();

                public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string> { ["a"] = "b" };

                public byte[] Payload { get; set; } = new byte[64];
            }

            public class Line
            {
                [Range(1, 10)]
                public int Quantity { get; set; }
            }

            private static Order Build(bool valid) =>
                new Order
                {
                    Number = valid ? "A-1" : null,
                    Lines = new List<Line>
                    {
                        new Line { Quantity = 1 },
                        new Line { Quantity = valid ? 2 : 0 },
                    },
                };

            [Fact]
            public async Task One_validator_gives_the_right_result_to_every_thread()
            {
                var validator = new RecursiveDataAnnotationValidator();
                var start = new TaskCompletionSource<bool>();

                var tasks = Enumerable.Range(0, 400).Select(i => Task.Run(async () =>
                {
                    await start.Task;
                    var expectValid = i % 2 == 0;
                    var results = new List<ValidationResult>();
                    var valid = validator.TryValidateObjectRecursive(Build(expectValid), results);
                    return (ExpectValid: expectValid, Valid: valid, Errors: ResultText.Describe(results));
                })).ToList();

                start.SetResult(true);
                var outcomes = await Task.WhenAll(tasks);

                var invalidErrors = ResultText.Expect(
                    "Number | The Number field is required.",
                    "Lines[1].Quantity | The field Quantity must be between 1 and 10.");
                foreach (var outcome in outcomes)
                {
                    Assert.Equal(outcome.ExpectValid, outcome.Valid);
                    Assert.Equal(outcome.ExpectValid ? new List<string>() : invalidErrors, outcome.Errors);
                }
            }
        }

        /// <summary>
        /// Use case: a file-format library that reads an XML document with XmlSerializer.Deserialize,
        /// then calls the overload that takes only the object and a results list, and joins the
        /// messages. The same library does this with JSON.
        /// This test does the same round trip with a model of the same kind: an element, a
        /// repeated element bound to a List, a wrapped array, an attribute, an optional element
        /// bound to a Nullable int, and repeated text elements bound to a List of string.
        /// XmlSerializer creates every object itself, so the classes need public setters and
        /// a public parameterless constructor.
        /// See: https://learn.microsoft.com/dotnet/api/system.xml.serialization.xmlserializer
        /// </summary>
        public class XmlDeserializedModels
        {
            [XmlRoot("Markup")]
            public class Markup
            {
                [XmlElement("Topic")]
                public Topic Topic { get; set; }

                [XmlElement("Comment")]
                public List<Comment> Comments { get; set; } = new List<Comment>();

                [XmlArray("Viewpoints")]
                [XmlArrayItem("ViewPoint")]
                public ViewPoint[] Viewpoints { get; set; }
            }

            public class Topic
            {
                [Required]
                [XmlAttribute("Guid")]
                public string Guid { get; set; }

                [Range(1, 5)]
                [XmlElement("Priority")]
                public int? Priority { get; set; }

                [EnumerableStringNotNullOrWhitespace]
                [XmlElement("Label")]
                public List<string> Labels { get; set; } = new List<string>();
            }

            public class Comment
            {
                [Required]
                [XmlElement("Text")]
                public string Text { get; set; }
            }

            public class ViewPoint
            {
                [Required]
                [XmlAttribute("Guid")]
                public string Guid { get; set; }
            }

            private static Markup Parse(string xml)
            {
                using (var reader = new StringReader(xml))
                {
                    return (Markup)new XmlSerializer(typeof(Markup)).Deserialize(reader);
                }
            }

            [Fact]
            public void Valid_document_passes()
            {
                var markup = Parse(
                    "<Markup><Topic Guid=\"t1\"><Priority>3</Priority><Label>a</Label></Topic>"
                    + "<Comment><Text>hi</Text></Comment><Viewpoints><ViewPoint Guid=\"v1\"/></Viewpoints></Markup>");

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(markup, results);

                Assert.True(valid);
                Assert.Empty(results);
            }

            [Fact]
            public void Invalid_document_reports_every_error_with_its_path()
            {
                var markup = Parse(
                    "<Markup><Topic><Priority>9</Priority><Label>a</Label><Label/></Topic>"
                    + "<Comment><Text>hi</Text></Comment><Comment/><Viewpoints><ViewPoint/></Viewpoints></Markup>");

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(markup, results);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Topic.Guid | The Guid field is required.",
                        "Topic.Priority | The field Priority must be between 1 and 5.",
                        "Topic.Labels | Found elements that are null or whitespace.",
                        "Comments[1].Text | The Text field is required.",
                        "Viewpoints[0].Guid | The Guid field is required."),
                    ResultText.Describe(results));
            }

            [Fact]
            public void Missing_optional_parts_pass()
            {
                // XmlSerializer leaves a missing element null: Topic here, and the Viewpoints array.
                var markup = Parse("<Markup/>");

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(markup, results);

                Assert.True(valid);
                Assert.Empty(results);
            }
        }

        /// <summary>
        /// Use case: the options validator above builds the context with a null service provider
        /// and null items. A caller can also pass items,
        /// or a service provider. The public overloads keep only validationContext.Items.
        /// The validator then builds a new ValidationContext for each object it visits.
        /// - Items reach every object, including nested ones, so an IValidatableObject deep in the
        ///   graph can read them.
        /// - The service provider does not. Each new context has a null provider, so an attribute
        ///   that calls validationContext.GetService gets null for every object, root included.
        ///   The callers found pass a context without a provider, so none is affected today.
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.items
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.getservice
        /// </summary>
        public class ValidationContextFlow
        {
            public class TenantAwareChild : IValidatableObject
            {
                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    if (!validationContext.Items.TryGetValue("tenant", out var tenant) || !Equals(tenant, "acme"))
                    {
                        yield return new ValidationResult("The tenant was not passed to the nested object.", new[] { "Tenant" });
                    }
                }
            }

            public class TenantRoot
            {
                public TenantAwareChild Child { get; set; } = new TenantAwareChild();

                public List<TenantAwareChild> Children { get; set; } = new List<TenantAwareChild> { new TenantAwareChild() };
            }

            public interface IBannedWords
            {
                bool IsBanned(string word);
            }

            public class NotBannedAttribute : ValidationAttribute
            {
                protected override ValidationResult IsValid(object value, ValidationContext validationContext)
                {
                    var banned = (IBannedWords)validationContext.GetService(typeof(IBannedWords));
                    if (banned == null) return new ValidationResult("The IBannedWords service was not available.");

                    return banned.IsBanned((string)value) ? new ValidationResult("The word is banned.") : ValidationResult.Success;
                }
            }

            public class Comment
            {
                [NotBanned]
                public string Text { get; set; } = "hello";
            }

            private class Services : IServiceProvider
            {
                public object GetService(Type serviceType) =>
                    serviceType == typeof(IBannedWords) ? new NoBannedWords() : null;
            }

            private class NoBannedWords : IBannedWords
            {
                public bool IsBanned(string word) => false;
            }

            [Fact]
            public void Context_items_reach_nested_objects_and_collection_items()
            {
                var root = new TenantRoot();
                var context = new ValidationContext(root, null, new Dictionary<object, object> { ["tenant"] = "acme" });

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(root, context, results);

                Assert.True(valid);
                Assert.Empty(results);
            }

            [Fact]
            public void Missing_context_items_are_reported_on_each_nested_object()
            {
                var root = new TenantRoot();

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(root, new ValidationContext(root, null, null), results);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Child.Tenant | The tenant was not passed to the nested object.",
                        "Children[0].Tenant | The tenant was not passed to the nested object."),
                    ResultText.Describe(results));
            }

            [Fact(Skip = "Not fixed yet. The service provider of the caller's context is not passed on, so GetService returns null.")]
            public void Service_provider_of_the_context_reaches_attributes()
            {
                var comment = new Comment();
                var context = new ValidationContext(comment, new Services(), null);

                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(comment, context, results);

                Assert.True(valid);
                Assert.Empty(results);
            }
        }

        /// <summary>
        /// Use case: services and tools that validate settings bound from configuration, at startup
        /// or through the options system. A settings class usually holds a required nested section,
        /// a list of sections, a string array, a dictionary of strings, a TimeSpan, and since C# 9 a
        /// positional record with `[property: ...]` attributes.
        /// Some callers turn each result into text with the member names joined by a colon,
        /// so the member names are part of what they show their users.
        /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record#positional-syntax-for-property-definition
        /// </summary>
        public class OptionsShapedSettings
        {
            public class AppSettings
            {
                [Required]
                public ServerSettings Server { get; set; }

                public List<EndpointSettings> Endpoints { get; set; } = new List<EndpointSettings>();

                public string[] AllowedHosts { get; set; } = new string[0];

                public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

                public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

                public DatabaseSettings Database { get; set; }
            }

            public class ServerSettings
            {
                [Required]
                public string Host { get; set; }

                [Range(1, 65535)]
                public int Port { get; set; } = 80;
            }

            public class EndpointSettings
            {
                [Required]
                public string Name { get; set; }
            }

            public sealed record DatabaseSettings([property: Required] string Host, [property: Range(1, 65535)] int Port);

            private static (bool Valid, List<string> Errors) Run(AppSettings settings)
            {
                var results = new List<ValidationResult>();
                var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(
                    settings,
                    new ValidationContext(settings, null, null),
                    results);
                return (valid, ResultText.Describe(results));
            }

            [Fact]
            public void Valid_settings_pass()
            {
                var settings = new AppSettings
                {
                    Server = new ServerSettings { Host = "localhost" },
                    Endpoints = new List<EndpointSettings> { new EndpointSettings { Name = "a" } },
                    AllowedHosts = new[] { "*" },
                    Tags = new Dictionary<string, string> { ["env"] = "dev" },
                    Database = new DatabaseSettings("db", 5432),
                };

                var (valid, errors) = Run(settings);

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Missing_required_section_is_reported_on_the_section()
            {
                var (valid, errors) = Run(new AppSettings { Server = null });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Server | The Server field is required."), errors);
            }

            [Fact]
            public void Errors_in_nested_sections_a_list_and_a_record_have_full_paths()
            {
                var settings = new AppSettings
                {
                    Server = new ServerSettings { Host = null, Port = 0 },
                    Endpoints = new List<EndpointSettings> { new EndpointSettings { Name = "a" }, new EndpointSettings() },
                    Database = new DatabaseSettings(null, 70000),
                };

                var (valid, errors) = Run(settings);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Server.Host | The Host field is required.",
                        "Server.Port | The field Port must be between 1 and 65535.",
                        "Endpoints[1].Name | The Name field is required.",
                        "Database.Host | The Host field is required.",
                        "Database.Port | The field Port must be between 1 and 65535."),
                    errors);
            }
        }
    }
}
