using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// How to validate a Uri, and where the limits are.
    /// The walk does not read the properties that Uri declares, because Segments throws on a
    /// relative Uri (see IsUnsafeToWalk and WhyUriPropertiesAreNotWalked below). That does not stop
    /// a Uri from being validated:
    /// - An attribute on a Uri property belongs to the class that declares the property. The
    ///   framework's Validator runs it when it validates that class, and passes it the Uri as the
    ///   value. This happens before the walk reaches the Uri, so the deny list does not affect it.
    /// - The class that holds the Uri can implement IValidatableObject and check the Uri there.
    /// - A subclass of Uri is validated like any other object, and the properties it adds are
    ///   walked. Only the properties declared by Uri itself are skipped.
    /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationattribute.isvalid
    /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.ivalidatableobject
    /// </summary>
    public class UriValidationTests
    {
        /// <summary>
        /// Checks that a Uri is absolute and that its port is in a range.
        /// It checks IsAbsoluteUri first, because Port throws InvalidOperationException on a
        /// relative Uri. The result names the property (validationContext.MemberName), so the
        /// recursive validator can put the path in front of it.
        /// See: https://learn.microsoft.com/dotnet/api/system.uri.isabsoluteuri
        /// </summary>
        public class PortRangeAttribute : ValidationAttribute
        {
            public PortRangeAttribute(int minimum, int maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            public int Minimum { get; }

            public int Maximum { get; }

            protected override ValidationResult IsValid(object value, ValidationContext validationContext)
            {
                // Like the framework's attributes, leave a null value to [Required].
                if (value == null) return ValidationResult.Success;

                var uri = (Uri)value;
                var memberNames = new[] { validationContext.MemberName };

                if (!uri.IsAbsoluteUri)
                    return new ValidationResult("The URI must be absolute to have a port.", memberNames);

                if (uri.Port < Minimum || uri.Port > Maximum)
                    return new ValidationResult($"The port {uri.Port} is outside {Minimum}-{Maximum}.", memberNames);

                return ValidationResult.Success;
            }
        }

        /// <summary>The same port check, without the IsAbsoluteUri check. Used to show what happens.</summary>
        public class UncheckedPortRangeAttribute : ValidationAttribute
        {
            public override bool IsValid(object value) =>
                value == null || ((Uri)value).Port >= 1025 && ((Uri)value).Port <= 2000;
        }

        public class HttpsOnlyAttribute : ValidationAttribute
        {
            public HttpsOnlyAttribute() : base("The URI must use https.")
            {
            }

            public override bool IsValid(object value) =>
                value == null || ((Uri)value).IsAbsoluteUri && ((Uri)value).Scheme == Uri.UriSchemeHttps;
        }

        public class Endpoint
        {
            [Required]
            [PortRange(1025, 2000)]
            public Uri Address { get; set; }
        }

        public class Settings
        {
            public Endpoint Primary { get; set; }

            public List<Endpoint> Fallbacks { get; set; } = new List<Endpoint>();
        }

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        /// <summary>
        /// Attributes on a Uri property run, on the root object and on nested objects. The path in
        /// a nested result comes from the recursive validator, like any other nested property.
        /// </summary>
        public class AttributesOnUriProperties
        {
            public class Webhook
            {
                [HttpsOnly]
                public Uri Callback { get; set; }
            }

            [Fact]
            public void Port_inside_the_range_passes()
            {
                var (valid, errors) = Run(new Endpoint { Address = new Uri("https://example.com:1500/") });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Theory]
            [InlineData("https://example.com:1024/", 1024)]
            [InlineData("https://example.com:2001/", 2001)]
            [InlineData("http://example.com:80/", 80)]
            public void Port_outside_the_range_fails(string address, int port)
            {
                var (valid, errors) = Run(new Endpoint { Address = new Uri(address) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"Address | The port {port} is outside 1025-2000."), errors);
            }

            [Fact]
            public void Required_uri_that_is_null_fails()
            {
                var (valid, errors) = Run(new Endpoint { Address = null });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Address | The Address field is required."), errors);
            }

            [Fact]
            public void Relative_uri_fails_the_port_check_without_throwing()
            {
                var (valid, errors) = Run(new Endpoint { Address = new Uri("/api", UriKind.Relative) });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Address | The URI must be absolute to have a port."), errors);
            }

            [Fact]
            public void Scheme_check_on_a_uri_property_runs()
            {
                var (valid, errors) = Run(new Webhook { Callback = new Uri("http://example.com/hook") });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Callback | The URI must use https."), errors);
            }

            [Fact]
            public void Uri_in_a_nested_object_and_in_collection_items_is_validated()
            {
                var settings = new Settings
                {
                    Primary = new Endpoint { Address = new Uri("https://example.com:80/") },
                    Fallbacks = new List<Endpoint>
                    {
                        new Endpoint { Address = new Uri("https://example.com:1500/") },
                        new Endpoint { Address = new Uri("https://example.com:3000/") },
                    },
                };

                var (valid, errors) = Run(settings);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Primary.Address | The port 80 is outside 1025-2000.",
                        "Fallbacks[1].Address | The port 3000 is outside 1025-2000."),
                    errors);
            }
        }

        /// <summary>
        /// The class that holds a Uri can check it in IValidatableObject.Validate. This suits checks
        /// that compare the Uri with other properties. The validator calls Validate only when the
        /// object's property attributes all pass, which is the framework's Validator rule.
        /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validator.tryvalidateobject
        /// </summary>
        public class ValidatableObjectThatHoldsAUri
        {
            public class Callback : IValidatableObject
            {
                public string AllowedHost { get; set; } = "example.com";

                public Uri Address { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    if (Address != null && (!Address.IsAbsoluteUri || Address.Host != AllowedHost))
                    {
                        yield return new ValidationResult($"The host must be {AllowedHost}.", new[] { nameof(Address) });
                    }
                }
            }

            public class Holder
            {
                public Callback Callback { get; set; }
            }

            [Fact]
            public void Uri_with_the_allowed_host_passes()
            {
                var (valid, errors) = Run(new Holder { Callback = new Callback { Address = new Uri("https://example.com/hook") } });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Uri_with_another_host_fails_in_a_nested_object()
            {
                var (valid, errors) = Run(new Holder { Callback = new Callback { Address = new Uri("https://evil.example/hook") } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Callback.Address | The host must be example.com."), errors);
            }
        }

        /// <summary>
        /// Why the deny list holds Uri. The framework's Validator reads a property's value only when
        /// the property has a validation attribute, and Uri's own properties have none. So the
        /// framework never reads Segments or the other members that throw on a relative Uri.
        /// The recursive validator reads every walked property of every object it visits, to look
        /// for nested objects. A walked property is one of a reference type other than string, and
        /// on Uri that is only Segments, a string[]. Reading it threw on a relative Uri. The crash
        /// came from the recursion, not from the framework, so the walk now skips the properties
        /// Uri declares and nothing else.
        /// See: https://github.com/dotnet/runtime/blob/main/src/libraries/System.ComponentModel.Annotations/src/System/ComponentModel/DataAnnotations/Validator.cs (GetPropertyValues)
        /// See: https://learn.microsoft.com/dotnet/api/system.uri.segments
        /// </summary>
        public class WhyUriPropertiesAreNotWalked
        {
            [Fact]
            public void Framework_validator_does_not_read_the_properties_of_a_relative_uri()
            {
                var relative = new Uri("/api", UriKind.Relative);
                var holder = new Endpoint { Address = relative };

                var results = new List<ValidationResult>();
                var ex = Record.Exception(() =>
                {
                    Validator.TryValidateObject(relative, new ValidationContext(relative), results, true);
                    Validator.TryValidateObject(holder, new ValidationContext(holder), results, true);
                });

                Assert.Null(ex);
            }

            [Fact]
            public void Reading_segments_of_a_relative_uri_throws()
            {
                var relative = new Uri("/api", UriKind.Relative);

                Assert.Throws<InvalidOperationException>(() => relative.Segments);
            }
        }

        /// <summary>
        /// Limits. They come from Uri, from the framework's attributes, and from the rule that an
        /// attribute on a property gets the property's whole value. Each test passes today. If a
        /// behavior changes, the test fails, so the change is deliberate.
        /// </summary>
        public class Limits
        {
            /// <summary>
            /// Uri.Port returns the scheme's default port when the URI has none, such as 443 for
            /// https. A range check sees that port, and IsDefaultPort tells the two cases apart.
            /// See: https://learn.microsoft.com/dotnet/api/system.uri.port
            /// See: https://learn.microsoft.com/dotnet/api/system.uri.isdefaultport
            /// </summary>
            [Fact]
            public void Uri_without_a_port_is_checked_with_the_default_port_of_its_scheme()
            {
                var uri = new Uri("https://example.com/");

                var (valid, errors) = Run(new Endpoint { Address = uri });

                Assert.True(uri.IsDefaultPort);
                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Address | The port 443 is outside 1025-2000."), errors);
            }

            public class UncheckedEndpoint
            {
                [UncheckedPortRange]
                public Uri Address { get; set; }
            }

            /// <summary>
            /// The validator does not catch an exception that an attribute throws. Uri.Port throws
            /// InvalidOperationException on a relative Uri, so an attribute that reads Port must
            /// check IsAbsoluteUri first, as PortRangeAttribute does.
            /// The framework's Validator calls the attribute directly, so the exception is not
            /// wrapped in a TargetInvocationException.
            /// </summary>
            [Fact]
            public void Attribute_that_reads_port_of_a_relative_uri_throws_to_the_caller()
            {
                Assert.Throws<InvalidOperationException>(() =>
                    Run(new UncheckedEndpoint { Address = new Uri("/api", UriKind.Relative) }));
            }

            public class UrlStringHolder
            {
                [Url]
                public Uri Address { get; set; }
            }

            /// <summary>
            /// What the framework's [Url] attribute does with a Uri depends on the target framework.
            /// - On .NET 10 it accepts an absolute Uri with the http, https or ftp scheme, and
            ///   rejects any other Uri, a relative one included.
            /// - On .NET 8 and .NET Framework it checks strings only, so it rejects every Uri.
            /// So [Url] on a Uri property is not portable. Use it on a string property, or write an
            /// attribute for Uri such as HttpsOnlyAttribute.
            /// NET10_0_OR_GREATER is a preprocessor symbol the SDK defines for .NET 10 and later.
            /// See: https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.urlattribute
            /// See: https://learn.microsoft.com/dotnet/standard/frameworks#preprocessor-symbols
            /// </summary>
            [Theory]
#if NET10_0_OR_GREATER
            [InlineData("https://example.com/", true)]
            [InlineData("ftp://example.com/", true)]
#else
            [InlineData("https://example.com/", false)]
            [InlineData("ftp://example.com/", false)]
#endif
            [InlineData("mailto:someone@example.com", false)]
            [InlineData("/relative", false)]
            public void Url_attribute_on_a_uri_depends_on_the_target_framework(string address, bool expected)
            {
                var (valid, _) = Run(new UrlStringHolder { Address = new Uri(address, UriKind.RelativeOrAbsolute) });

                Assert.Equal(expected, valid);
            }

            public class Mirrors
            {
                public List<Uri> Addresses { get; set; }
            }

            /// <summary>
            /// The items of a List of Uri get no checks, because a Uri has no validation of its own,
            /// and an attribute on the list property gets the whole list as its value.
            /// To check each Uri, hold it in a class with an attribute on the property, such as
            /// the List of Endpoint in Settings, or check the list in IValidatableObject.Validate.
            /// </summary>
            [Fact]
            public void Uris_in_a_list_are_not_checked_on_their_own()
            {
                var (valid, errors) = Run(new Mirrors
                {
                    Addresses = new List<Uri> { new Uri("/relative", UriKind.Relative), new Uri("http://example.com:80/") },
                });

                Assert.True(valid);
                Assert.Empty(errors);
            }

        }

        /// <summary>
        /// A subclass of Uri is validated like any other object: its attributes and its
        /// IValidatableObject.Validate run, and the properties it adds are walked. The walk skips
        /// only the properties that Uri declares, such as Segments, so a relative subclass does
        /// not throw. A property declared in a System namespace counts as Uri's, and one declared
        /// by the subclass does not (see IsUnsafeToWalk).
        /// Release 2.2.0 validated a Uri subclass too, but threw for a relative one.
        /// </summary>
        public class UriSubclasses
        {
            public class Owner
            {
                [Required]
                public string Team { get; set; }
            }

            public class ServiceUri : Uri, IValidatableObject
            {
                public ServiceUri(string uriString) : base(uriString, UriKind.RelativeOrAbsolute)
                {
                }

                public Owner Owner { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    if (!IsAbsoluteUri || Port < 1025 || Port > 2000)
                    {
                        yield return new ValidationResult("The service URI needs a port in 1025-2000.", new[] { "Port" });
                    }
                }
            }

            public class Holder
            {
                public Uri Address { get; set; }
            }

            [Fact]
            public void Valid_subclass_passes()
            {
                var address = new ServiceUri("https://example.com:1500/") { Owner = new Owner { Team = "web" } };

                var (valid, errors) = Run(new Holder { Address = address });

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Validate_of_a_subclass_runs()
            {
                var (valid, errors) = Run(new Holder { Address = new ServiceUri("https://example.com:80/") });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Address.Port | The service URI needs a port in 1025-2000."), errors);
            }

            [Fact]
            public void Property_added_by_a_subclass_is_walked()
            {
                var address = new ServiceUri("https://example.com:1500/") { Owner = new Owner { Team = null } };

                var (valid, errors) = Run(new Holder { Address = address });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Address.Owner.Team | The Team field is required."), errors);
            }

            [Fact]
            public void Relative_subclass_is_validated_without_throwing()
            {
                var address = new ServiceUri("/api") { Owner = new Owner { Team = null } };

                var valid = true;
                var errors = new List<string>();
                var ex = Record.Exception(() => (valid, errors) = Run(new Holder { Address = address }));

                Assert.Null(ex);
                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        "Address.Owner.Team | The Team field is required.",
                        "Address.Port | The service URI needs a port in 1025-2000."),
                    errors);
            }
        }
    }
}
