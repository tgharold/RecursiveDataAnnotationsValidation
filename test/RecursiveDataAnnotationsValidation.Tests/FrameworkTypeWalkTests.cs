using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using RecursiveDataAnnotationsValidation.Tests;
using Xunit;

// A type in the global namespace. Type.Namespace is null for it, which IsInSystemNamespace must
// treat as "not a System namespace". See:
// https://learn.microsoft.com/dotnet/api/system.type.namespace
internal class GlobalNamespaceUri : Uri
{
    public GlobalNamespaceUri(string uriString) : base(uriString)
    {
    }

    public FrameworkTypeWalkTests.Leaf Owner { get; set; }
}

// The namespace starts with the letters "System" but is not System or System.*, so the deny
// list must not treat these types as framework types.
namespace SystemLookalike
{
    internal class LookalikeUri : Uri
    {
        public LookalikeUri(string uriString) : base(uriString)
        {
        }

        public FrameworkTypeWalkTests.Leaf Owner { get; set; }
    }
}

namespace Systematic
{
    internal class SystematicUri : Uri
    {
        public SystematicUri(string uriString) : base(uriString)
        {
        }

        public FrameworkTypeWalkTests.Leaf Owner { get; set; }
    }
}

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// Framework objects that end up in a model by accident, such as a CultureInfo, an HttpClient
    /// or a ClaimsPrincipal on a settings class or a request wrapper.
    /// The validator reads every public property of an object that has a reference type, so each
    /// framework object has its whole property graph read. A getter that throws, or that returns a
    /// new object on every read, breaks the walk. The framework-type deny list (IsUnsafeToWalk)
    /// exists for the types where that happened: Type, Assembly, Uri, DirectoryInfo and a few more.
    ///
    /// These tests answer the next question: which other framework types are safe to hold? Each
    /// one is placed in a model next to an invalid sibling. The walk must finish, must not throw,
    /// and must report only the sibling's error. A hang fails the test after ten seconds instead of
    /// stopping the run. A stack overflow would stop the run, because .NET cannot catch it.
    ///
    /// The tests only use the public API. They all pass against the validator of v2.2.0, except
    /// the ones in FixedSince23, which fail there on purpose.
    /// See: https://learn.microsoft.com/dotnet/api/system.reflection.propertyinfo.getvalue
    /// </summary>
    public class FrameworkTypeWalkTests
    {
        public class Leaf
        {
            [Required(ErrorMessage = "Name is required")]
            public string Name { get; set; }
        }

        public class Holder
        {
            public object Value { get; set; }

            public Leaf Sibling { get; set; } = new Leaf();
        }

        public class ItemHolder
        {
            public List<object> Items { get; set; }
        }

        // Runs the validator on its own thread, so a hang fails the test instead of the whole run.
        // The thread is a background thread, so a hung walk does not keep the test host alive.
        // A thread in the pool would also work, but a hung one would stay in the pool for good.
        // See: https://learn.microsoft.com/dotnet/api/system.threading.thread.isbackground
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
            }) { IsBackground = true };

            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The walk did not finish in ten seconds.");

            // Throw again with the original stack trace, so Assert.Throws sees the original type.
            // See: https://learn.microsoft.com/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();

            return (valid, ResultText.Describe(results));
        }

        /// <summary>
        /// Framework objects that the walk can read. Each value is created on demand. The test
        /// disposes nothing, because the objects hold no unmanaged resources that matter here,
        /// and Console.Out must not be disposed.
        /// </summary>
        private static readonly Dictionary<string, Func<object>> SafeToWalk = new Dictionary<string, Func<object>>
        {
            // Culture and text
            ["CultureInfo"] = () => System.Globalization.CultureInfo.InvariantCulture,
            ["DateTimeFormatInfo"] = () => System.Globalization.DateTimeFormatInfo.InvariantInfo,
            ["NumberFormatInfo"] = () => System.Globalization.NumberFormatInfo.InvariantInfo,
            ["CompareInfo"] = () => System.Globalization.CultureInfo.InvariantCulture.CompareInfo,
            ["TextInfo"] = () => System.Globalization.CultureInfo.InvariantCulture.TextInfo,
            ["Calendar"] = () => System.Globalization.CultureInfo.InvariantCulture.Calendar,
            ["Encoding"] = () => System.Text.Encoding.UTF8,
            ["Regex"] = () => new System.Text.RegularExpressions.Regex("a"),
            ["StringBuilder"] = () => new System.Text.StringBuilder("x"),
            ["StringComparer"] = () => StringComparer.OrdinalIgnoreCase,
            ["EqualityComparer"] = () => EqualityComparer<string>.Default,
            ["TimeZoneInfo.Utc"] = () => TimeZoneInfo.Utc,
            ["TimeZoneInfo.Local"] = () => TimeZoneInfo.Local,

            // Environment, threading and diagnostics
            ["Version"] = () => new Version(1, 2),
            ["AppDomain"] = () => AppDomain.CurrentDomain,
            ["OperatingSystem"] = () => Environment.OSVersion,
            ["Console.Out"] = () => Console.Out,
            ["Random"] = () => new Random(1),
            ["Stopwatch"] = () => System.Diagnostics.Stopwatch.StartNew(),
            ["StackTrace"] = () => new System.Diagnostics.StackTrace(),
            ["StackFrame"] = () => new System.Diagnostics.StackFrame(0),
            ["SemaphoreSlim"] = () => new SemaphoreSlim(1),
            ["ManualResetEvent"] = () => new ManualResetEvent(false),
            ["CancellationTokenSource"] = () => new CancellationTokenSource(),
            ["CancellationToken"] = () => CancellationToken.None,
            ["SynchronizationContext"] = () => new SynchronizationContext(),
            ["WeakReference"] = () => new WeakReference(new object()),
            ["Timer"] = () => new System.Timers.Timer(),
            ["Component"] = () => new System.ComponentModel.Component(),
            ["BackgroundWorker"] = () => new System.ComponentModel.BackgroundWorker(),

            // Network
            ["IPAddress"] = () => System.Net.IPAddress.Loopback,
            ["IPEndPoint"] = () => new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 80),
            ["WebHeaderCollection"] = () => new System.Net.WebHeaderCollection(),
            ["CookieContainer"] = () => new System.Net.CookieContainer(),

            // Streams, readers and documents
            ["MemoryStream"] = () => new System.IO.MemoryStream(),
            ["StringReader"] = () => new System.IO.StringReader("x"),
            ["XmlDocument"] = () =>
            {
                var document = new System.Xml.XmlDocument();
                document.LoadXml("<a><b/></a>");
                return document;
            },
            ["XDocument"] = () => System.Xml.Linq.XDocument.Parse("<a><b/></a>"),
            ["DataTable"] = () => new System.Data.DataTable("t"),
            ["DataSet"] = () => new System.Data.DataSet("s"),
            ["NameValueCollection"] = () => new System.Collections.Specialized.NameValueCollection { { "a", "b" } },
            ["BitArray"] = () => new System.Collections.BitArray(4),

            // Identity. ClaimsPrincipal is not here, see FixedSince23.
            ["ClaimsIdentity"] = () => new System.Security.Claims.ClaimsIdentity("test"),

            // Values that become boxed objects when a property is typed as object
            ["Guid"] = () => Guid.NewGuid(),
            ["Enum"] = () => DayOfWeek.Monday,
            ["ArraySegment"] = () => new ArraySegment<byte>(new byte[2]),
            ["int[,]"] = () => new int[2, 2],
            ["object[,]"] = () => new object[2, 2],
            ["object"] = () => new object(),

            // Tasks of a value type have no reference-type result to read. Task<object> is
            // different, see FaultedAndCanceledTasks below.
            ["Completed Task<int>"] = () => Task.FromResult(1),
            ["Faulted Task<int>"] = () => Task.FromException<int>(new Exception("faulted")),
            ["Canceled Task<int>"] = () => Task.FromCanceled<int>(new CancellationToken(true)),
            ["TaskCompletionSource<int>"] = () => new TaskCompletionSource<int>(),
            ["Exception with inner"] = () => new Exception("outer", new Exception("inner")),
            ["AggregateException"] = () => new AggregateException(new Exception("one")),

#if NET8_0_OR_GREATER
            // These types are not available to the .NET Framework 4.8.1 build without a package or
            // an assembly reference.
            ["HttpClient"] = () => new System.Net.Http.HttpClient(),
            ["HttpResponseMessage"] = () => new System.Net.Http.HttpResponseMessage(),
            ["Memory<byte>"] = () => new Memory<byte>(new byte[2]),
            ["JsonDocument"] = () => System.Text.Json.JsonDocument.Parse("{\"a\":[1,2]}"),
            ["JsonElement"] = () => System.Text.Json.JsonDocument.Parse("{\"a\":[1,2]}").RootElement,
            ["JsonNode"] = () => System.Text.Json.Nodes.JsonNode.Parse("{\"a\":{\"b\":1}}"),
            ["JsonSerializerOptions"] = () => new System.Text.Json.JsonSerializerOptions(),
            ["Activity"] = () => new System.Diagnostics.Activity("probe"),
#endif
        };

        public static IEnumerable<object[]> SafeToWalkNames() => SafeToWalk.Keys.Select(name => new object[] { name });

        /// <summary>
        /// The walk finishes, does not throw, and reports only the invalid sibling, with the
        /// path from the root. Run twice: the object as a property value, and as an item of a
        /// collection. The two shapes use different branches of the walk.
        /// </summary>
        [Theory]
        [MemberData(nameof(SafeToWalkNames))]
        public void Framework_object_is_walked_without_error(string name)
        {
            AssertWalkedWithoutError(SafeToWalk[name]);
        }

        // create is called once for each shape, so that each run gets a new object.
        private static void AssertWalkedWithoutError(Func<object> create)
        {
            var asProperty = Run(new Holder { Value = create() });

            Assert.False(asProperty.Valid);
            Assert.Equal(ResultText.Expect("Sibling.Name | Name is required"), asProperty.Errors);

            var asItem = Run(new ItemHolder { Items = new List<object> { create(), new Leaf() } });

            Assert.False(asItem.Valid);
            Assert.Equal(ResultText.Expect("Items[1].Name | Name is required"), asItem.Errors);
        }

        /// <summary>
        /// Framework types that v2.2.0 could not walk and the deny list lets 2.3 walk. These tests
        /// fail against the validator of v2.2.0 on purpose.
        /// ClaimsPrincipal: v2.2.0 throws InvalidOperationException ("Method may only be called on
        /// a Type for which Type.IsGenericParameter is true"), from Type.DeclaringMethod. The walk
        /// reaches a Type object. A ClaimsPrincipal is a realistic model member, for example the
        /// user on a request wrapper.
        /// Thread and Process: reading their properties throws, because the object only works for
        /// the thread or process that created it. Neither is a model type in a real project, but
        /// the walk should not throw when one is held.
        /// </summary>
        public class FixedSince23
        {
            [Fact]
            public void ClaimsPrincipal_is_walked_without_error()
            {
                AssertWalkedWithoutError(() => new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim("role", "admin") }, "test")));
            }

            // Run walks on a new thread, not on the thread that Value represents. Some Thread
            // properties throw InvalidOperationException ("This operation must be performed on
            // the same thread as that represented by the Thread instance") when read elsewhere.
            [Fact]
            public void Thread_object_is_walked_without_error()
            {
                var (valid, errors) = Run(new Holder { Value = Thread.CurrentThread });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Sibling.Name | Name is required"), errors);
            }

            [Fact]
            public void Thread_in_a_list_is_walked_without_error()
            {
                var (valid, errors) = Run(new ItemHolder
                {
                    Items = new List<object> { Thread.CurrentThread, new Leaf() }
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Items[1].Name | Name is required"), errors);
            }

            // Process.GetCurrentProcess() throws InvalidOperationException from a property read.
            // Process.StartInfo says "Process was not started by this object", because this
            // process was not started through a Process object.
            [Fact]
            public void Process_object_is_walked_without_error()
            {
                var (valid, errors) = Run(new Holder { Value = System.Diagnostics.Process.GetCurrentProcess() });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Sibling.Name | Name is required"), errors);
            }

            [Fact]
            public void Process_in_a_list_is_walked_without_error()
            {
                var (valid, errors) = Run(new ItemHolder
                {
                    Items = new List<object> { System.Diagnostics.Process.GetCurrentProcess(), new Leaf() }
                });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Items[1].Name | Name is required"), errors);
            }
        }

        /// <summary>
        /// Limitation guards for a Task whose result is a reference type. The walk reads
        /// Task&lt;T&gt;.Result, and Result throws for a task that faulted or was canceled. The
        /// exception reaches the caller wrapped in a TargetInvocationException, because the
        /// validator reads the property with reflection.
        /// A task that has not finished is worse: Result blocks, so the walk hangs until the task
        /// ends. That case has no test, because it would hang the run.
        /// Whether the walk should read Result only for a task that ran to completion is an open
        /// decision. If it changes, these two tests fail on purpose, and the fix replaces them
        /// with the skipped specs in TaskPropertyTests, which expect a valid walk. A completed Task&lt;T&gt; is pinned elsewhere
        /// (OddShapeTests, Completed_task_property_is_validated_through_its_result).
        /// v2.2.0 behaves the same.
        /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.task-1.result
        /// See: https://learn.microsoft.com/dotnet/api/system.reflection.targetinvocationexception
        /// </summary>
        public class FaultedAndCanceledTasks
        {
            [Fact]
            public void Faulted_task_of_a_reference_type_throws_to_the_caller()
            {
                var task = Task.FromException<object>(new InvalidOperationException("faulted"));

                var thrown = Assert.Throws<TargetInvocationException>(() => Run(new Holder { Value = task }));

                var aggregate = Assert.IsType<AggregateException>(thrown.InnerException);
                Assert.IsType<InvalidOperationException>(Assert.Single(aggregate.InnerExceptions));
            }

            [Fact]
            public void Canceled_task_of_a_reference_type_throws_to_the_caller()
            {
                var task = Task.FromCanceled<object>(new CancellationToken(true));

                var thrown = Assert.Throws<TargetInvocationException>(() => Run(new Holder { Value = task }));

                var aggregate = Assert.IsType<AggregateException>(thrown.InnerException);
                Assert.IsType<TaskCanceledException>(Assert.Single(aggregate.InnerExceptions));
            }
        }

        /// <summary>
        /// A subclass of Uri that is not in a System namespace is a user type. The walk reads the
        /// properties it adds, like any model property, and skips only the properties that Uri
        /// itself declares. The checks that decide this look at the namespace of the declaring type:
        /// - A type in the global namespace has a null Namespace.
        /// - A namespace such as SystemLookalike or Systematic begins with "System" but is not
        ///   System or System.*. A check that used StartsWith("System") alone would wrongly skip it.
        /// The Uri is absolute, so v2.2.0 can read all of its properties and gives the same answer.
        /// See: https://learn.microsoft.com/dotnet/api/system.type.namespace
        /// See: https://learn.microsoft.com/dotnet/csharp/language-reference/keywords/namespace
        /// </summary>
        public class UserSubclassesOfUri
        {
            public static IEnumerable<object[]> Subclasses() => new[]
            {
                new object[] { "global namespace", (Func<Leaf, object>)(owner => new GlobalNamespaceUri("https://example.com/") { Owner = owner }) },
                new object[] { "SystemLookalike", (Func<Leaf, object>)(owner => new SystemLookalike.LookalikeUri("https://example.com/") { Owner = owner }) },
                new object[] { "Systematic", (Func<Leaf, object>)(owner => new Systematic.SystematicUri("https://example.com/") { Owner = owner }) },
            };

            [Theory]
            [MemberData(nameof(Subclasses))]
            public void Property_added_by_the_subclass_is_walked(string name, Func<Leaf, object> create)
            {
                Assert.NotNull(name);

                var (valid, errors) = Run(new Holder { Value = create(new Leaf()), Sibling = new Leaf { Name = "ok" } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect("Value.Owner.Name | Name is required"), errors);
            }

            [Theory]
            [MemberData(nameof(Subclasses))]
            public void Valid_subclass_passes(string name, Func<Leaf, object> create)
            {
                Assert.NotNull(name);

                var (valid, errors) = Run(new Holder { Value = create(new Leaf { Name = "ok" }), Sibling = new Leaf { Name = "ok" } });

                Assert.True(valid);
                Assert.Empty(errors);
            }
        }
    }
}
