using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// Open decision, not a bug report: what the walk should do with a Task&lt;T&gt; property
    /// whose task did not run to completion. Every test in this file is skipped on purpose.
    /// They record the behavior that one of the two options would give. They do not say that the
    /// behavior must change. Nobody has decided, and a wrong decision would have to be reversed
    /// in a later release, so the library stays as it is until someone rules.
    ///
    /// What happens today (the same on v2.0.0, v2.2.0 and the current code):
    /// The validator reads every public property that has a reference type, and Task&lt;T&gt; has
    /// one: Result. Task&lt;int&gt; is not affected, because int is a value type, and a
    /// non-generic Task has no Result. For Task&lt;SomeClass&gt;, the state of the task decides:
    /// - Completed: Result is read and validated. The path is Value.Result.Name. A test in
    ///   OddShapeTests pins this (Completed_task_property_is_validated_through_its_result).
    /// - Faulted or canceled: Result throws an AggregateException. The caller sees a
    ///   TargetInvocationException, because the validator reads the property with reflection.
    ///   FrameworkTypeWalkTests.FaultedAndCanceledTasks pins this.
    /// - Not finished: Result blocks the calling thread until the task ends. If it never ends,
    ///   validation hangs. On a thread with a synchronization context, such as a UI thread in
    ///   WinForms or WPF, it can deadlock when the task needs that thread to finish. Nothing
    ///   pins this, because a test would hang the run. The specs below use a ten-second guard.
    ///
    /// Option 1, leave as is: write the behavior in the README. The risk stays with callers who
    /// keep a Task in a validated model. No real project found so far does.
    ///
    /// Option 3, read Result only when Task.Status is RanToCompletion, and skip it otherwise:
    /// - A faulted, canceled or unfinished task no longer throws or hangs.
    /// - A completed task behaves as before.
    /// - Cost: a task that is still running is not validated. Before, the walk waited, then
    ///   validated the result. A model that failed that way can now pass, a false pass. A result
    ///   that arrives later is only validated by the next walk, which the README would say.
    /// - The check looks at the object, not only at its type, so it cannot live in the
    ///   type-based deny list (see TypeExtensions.IsUnsafeToWalk). It is one status check in the
    ///   walk loop. A deny list for Task is ruled out, because it would drop validation of a
    ///   completed task's result, which every release since v2.0.0 reports.
    ///
    /// The tests below describe option 3. If the decision is option 1 instead, delete this file
    /// and keep the README note. If the decision is option 3:
    /// 1. Remove the Skip argument from each test.
    /// 2. Replace the two limitation guards in FrameworkTypeWalkTests.FaultedAndCanceledTasks
    ///    with these tests. The guards fail on purpose once Result is skipped.
    /// 3. Add a Fixed entry to the changelog and a README note about a task that is still running.
    ///
    /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.task-1.result
    /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskstatus
    /// See: https://learn.microsoft.com/dotnet/api/system.threading.synchronizationcontext
    /// </summary>
    public class TaskPropertyTests
    {
        private const string Undecided =
            "No decision yet. The walk reads Task<T>.Result for any task. Option 3 would read it only after RanToCompletion.";

        private const string SiblingRequired = "Sibling.Name | The Name field is required.";

        public class Leaf
        {
            [Required]
            public string Name { get; set; }
        }

        // The sibling is invalid on purpose. Each test expects exactly one error, from the
        // sibling. That proves the walk finished and carried on past the Task property.
        public class TaskHolder
        {
            public Task<Leaf> Value { get; set; }

            public Leaf Sibling { get; set; } = new Leaf();
        }

        // Runs the validator on its own thread, so that a walk which hangs fails the test after
        // ten seconds and does not stop the whole run. The thread is a background thread, so a
        // hung walk does not keep the test host alive.
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

            // Throw again with the original stack trace, so a test sees the original exception type.
            // See: https://learn.microsoft.com/dotnet/api/system.runtime.exceptionservices.exceptiondispatchinfo
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();

            return (valid, ResultText.Describe(results));
        }

        /// <summary>
        /// The task ended with an exception. Result would rethrow it, so option 3 skips Result.
        /// Task.FromException returns a task that is already faulted, so the test needs no timing.
        /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.fromexception
        /// </summary>
        public class FaultedTask
        {
            [Fact(Skip = Undecided)]
            public void Walk_finishes_and_reports_only_the_sibling()
            {
                var model = new TaskHolder { Value = Task.FromException<Leaf>(new InvalidOperationException("faulted")) };

                var (valid, errors) = Run(model);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect(SiblingRequired), errors);
            }
        }

        /// <summary>
        /// The task was canceled. Result throws for it too, so it needs the same treatment as a
        /// faulted task.
        /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.fromcanceled
        /// </summary>
        public class CanceledTask
        {
            [Fact(Skip = Undecided)]
            public void Walk_finishes_and_reports_only_the_sibling()
            {
                var model = new TaskHolder { Value = Task.FromCanceled<Leaf>(new CancellationToken(true)) };

                var (valid, errors) = Run(model);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect(SiblingRequired), errors);
            }
        }

        /// <summary>
        /// The task has not finished. A TaskCompletionSource gives a task that stays unfinished
        /// until the test completes it by hand, so the state is fixed and no clock is involved.
        /// Today the walk waits on Result and the ten-second guard in Run fails the test.
        /// The second test shows the cost of option 3: the result is not validated while the task
        /// runs, and the next walk validates it once the task has completed.
        /// See: https://learn.microsoft.com/dotnet/api/system.threading.tasks.taskcompletionsource-1
        /// </summary>
        public class UnfinishedTask
        {
            [Fact(Skip = Undecided)]
            public void Walk_finishes_and_reports_only_the_sibling()
            {
                var source = new TaskCompletionSource<Leaf>();

                var (valid, errors) = Run(new TaskHolder { Value = source.Task });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect(SiblingRequired), errors);
            }

            [Fact(Skip = Undecided)]
            public void Result_is_validated_by_a_later_walk_after_the_task_completes()
            {
                var source = new TaskCompletionSource<Leaf>();
                var model = new TaskHolder { Value = source.Task, Sibling = new Leaf { Name = "ok" } };

                // While the task runs, its result is skipped, so the model passes.
                var (validWhileRunning, errorsWhileRunning) = Run(model);

                Assert.True(validWhileRunning);
                Assert.Empty(errorsWhileRunning);

                // After the task completes, the same model fails, because Result is now read.
                source.SetResult(new Leaf());
                var (validAfter, errorsAfter) = Run(model);

                Assert.False(validAfter);
                Assert.Equal(ResultText.Expect("Value.Result.Name | The Name field is required."), errorsAfter);
            }
        }
    }
}
