using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BenchmarkDotNet.Attributes;
using RecursiveDataAnnotationsValidation;
using RecursiveDataAnnotationsValidation.Tests.TestModels;

namespace Benchmark
{
    public class Benchmarks
    {
        private static readonly IRecursiveDataAnnotationValidator _sut = new RecursiveDataAnnotationValidator();

        #region EnumerableExample

        private static readonly EnumerableExample EnumerableExamplePassesAllValidationNoChildren = new EnumerableExample
        {
            Name = "Passes all",
            Age = 75,
            Items = new List<ItemExample>(),
            ItemsList = new List<ItemExample>(),
            ItemsCollection = new List<ItemExample>(),
        };

        [Benchmark]
        public bool EnumerableExample_PassesAllValidation_NoChildren()
        {
            var validationResults = new List<ValidationResult>();
            var isValid =
                _sut.TryValidateObjectRecursive(EnumerableExamplePassesAllValidationNoChildren, validationResults);
            return isValid && validationResults.Any() == false;
        }

        private static readonly EnumerableExample EnumerableExamplePassesAllValidationAllChildren =
            new EnumerableExample
            {
                Name = "Passes all",
                Age = 75,
                Items = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 125,
                            StringB = "child-1-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 15,
                            StringB = "child-2-string-abc",
                            BoolC = false,
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                },
                ItemsList = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1L",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 123,
                            StringB = "child-1L-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2L",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 75,
                            StringB = "child-2L-string-abc",
                            BoolC = false,
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                },
                ItemsCollection = new List<ItemExample>
                {
                    new ItemExample
                    {
                        Name = "Child 1C",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 25,
                            StringB = "child-1C-stringB",
                            BoolC = true,
                            ExampleEnumD = ExampleEnum.ValueC
                        }
                    },
                    new ItemExample
                    {
                        Name = "Child 2C",
                        SimpleA = new SimpleExample
                        {
                            IntegerA = 120,
                            StringB = "child-2C-string-abc",
                            BoolC = false,
                            ExampleEnumD = ExampleEnum.ValueA
                        }
                    }
                }
            };

        [Benchmark]
        public bool EnumerableExample_PassesAllValidation_AllChildren()
        {
            var validationResults = new List<ValidationResult>();
            var isValid =
                _sut.TryValidateObjectRecursive(EnumerableExamplePassesAllValidationAllChildren, validationResults);
            return isValid && validationResults.Any() == false;
        }

        #endregion

        #region RecursionExample

        private static readonly RecursionExample RecursionExamplePassesAllValidation = new RecursionExample
        {
            Name = "Recursion1",
            BooleanA = false,
            Recursion = new RecursionExample
            {
                Name = "Recursion1.Inner1",
                BooleanA = true,
                Recursion = null
            }
        };

        public Benchmarks()
        {
            RecursionExamplePassesAllValidation.Recursion.Recursion = RecursionExamplePassesAllValidation.Recursion;
        }

        [Benchmark]
        public bool RecursionExample_PassesAllValidation()
        {
            var validationResults = new List<ValidationResult>();
            var isValid = _sut.TryValidateObjectRecursive(RecursionExamplePassesAllValidation, validationResults);
            return isValid && validationResults.Any() == false;
        }

        #endregion

        #region SimpleExample

        private static readonly SimpleExample SimpleExamplePassesAllValidation = new SimpleExample
        {
            IntegerA = 100,
            StringB = "test-100",
            BoolC = true,
            ExampleEnumD = ExampleEnum.ValueB
        };

        [Benchmark]
        public bool SimpleExample_PassesAllValidation()
        {
            var validationResults = new List<ValidationResult>();
            var isValid = _sut.TryValidateObjectRecursive(SimpleExamplePassesAllValidation, validationResults);
            return isValid && validationResults.Any() == false;
        }

        private static readonly SimpleExample SimpleExampleAllNull = new SimpleExample
        {
            IntegerA = null,
            StringB = null,
            BoolC = null,
            ExampleEnumD = null
        };

        [Benchmark]
        public bool SimpleExample_AllNull()
        {
            var validationResults = new List<ValidationResult>();
            var isValid = _sut.TryValidateObjectRecursive(SimpleExampleAllNull, validationResults);
            return isValid && validationResults.Any() == false;
        }

        #endregion

        #region SkippedChildrenExample

        private static readonly SkippedChildrenExample SkippedChildrenExamplePassesAllValidation =
            new SkippedChildrenExample
            {
                Name = "some name for pass all",
                SimpleA = new SimpleExample
                {
                    StringB = "simple A pass-all",
                    BoolC = false,
                    IntegerA = 125,
                    ExampleEnumD = ExampleEnum.ValueB,
                },
                SimpleB = new SimpleExample
                {
                    StringB = "simple B pass-all",
                    BoolC = true,
                    IntegerA = 95,
                    ExampleEnumD = ExampleEnum.ValueA,
                },
            };

        [Benchmark]
        public bool SkippedChildrenExample_PassesAllValidation()
        {
            var validationResults = new List<ValidationResult>();
            var isValid = _sut.TryValidateObjectRecursive(SkippedChildrenExamplePassesAllValidation, validationResults);
            return isValid && validationResults.Any() == false;
        }

        private static readonly SkippedChildrenExample SkippedChildrenExampleAllNull = new SkippedChildrenExample
        {
            Name = null,
            SimpleA = new SimpleExample
            {
                StringB = null,
                BoolC = null,
                IntegerA = null,
                ExampleEnumD = null,
            },
            SimpleB = null,
        };

        [Benchmark]
        public bool SkippedChildrenExample_AllNull()
        {
            var validationResults = new List<ValidationResult>();
            var isValid = _sut.TryValidateObjectRecursive(SkippedChildrenExampleAllNull, validationResults);
            return isValid && validationResults.Any() == false;
        }

        #endregion
    }
}