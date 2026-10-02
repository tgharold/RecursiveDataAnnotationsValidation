using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;

namespace RecursiveDataAnnotationsValidation.Tests
{
    /// <summary>
    /// The walk visits the shallowest objects first (a breadth-first walk) and not the first route
    /// it finds (a depth-first walk).
    ///
    /// The maximum depth is 128 (see MaxDepthTests). A depth-first walk measures the depth of the
    /// route it took, and a graph with links back to its parents has a long route even when it is
    /// shallow. An object that was validated by another, shorter route then still failed with
    /// "nested more than 128 levels deep", which was false. A breadth-first walk reaches each
    /// object by its shortest route, so the depth in that message is always true.
    ///
    /// Three rules follow:
    /// 1. The depth of an object is the length of its shortest path from the object that was passed in.
    ///    A property is one level and a collection index is one level.
    /// 2. An object that can be reached by two routes is reported with its shortest path. If the
    ///    routes are equally long, the first one wins: the property that comes first, and an item of
    ///    a collection before a property of the same object.
    /// 3. The results are in the order of the walk: shallowest objects first. The results of one
    ///    object stay together, in the order that the framework's Validator gives them.
    ///
    /// The tests build graphs that stay within the limit of the walk, so a depth-first walk
    /// fails them with an error and does not overflow the stack.
    /// </summary>
    public class BreadthFirstWalkTests
    {
        private const string TooDeep = "The object is nested more than 128 levels deep and was not validated.";

        private const string NameRequired = "Name is required";

        private static (bool Valid, List<string> Errors) Run(object model)
        {
            var results = new List<ValidationResult>();
            var valid = new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return (valid, ResultText.Describe(results));
        }

        // The results in the order that the validator returned them. ResultText.Describe sorts them.
        private static List<string> RunInOrder(object model)
        {
            var results = new List<ValidationResult>();
            new RecursiveDataAnnotationValidator().TryValidateObjectRecursive(model, results);
            return results.Select(r => $"{string.Join(",", r.MemberNames)} | {r.ErrorMessage}").ToList();
        }

        public class Node
        {
            public Node Next { get; set; }

            [Required(ErrorMessage = NameRequired)]
            public string Name { get; set; } = "ok";
        }

        /// <summary>
        /// A model of what an ORM such as Entity Framework loads: every order links to its lines,
        /// each line links back to its order and to a product, and each product links to the lines
        /// that use it. Here each order shares one product with the next order, so the whole store
        /// is one connected graph. The graph is shallow, because the store holds the orders directly,
        /// and the links back and sideways make a depth-first route as long as the number of orders.
        /// </summary>
        public class EfStore
        {
            public class Store
            {
                public List<Order> Orders { get; set; } = new List<Order>();
            }

            public class Order
            {
                [Required(ErrorMessage = "Code is required")]
                public string Code { get; set; } = "o";

                public Customer Customer { get; set; }

                public List<Line> Lines { get; set; } = new List<Line>();
            }

            public class Line
            {
                [Required(ErrorMessage = "Sku is required")]
                public string Sku { get; set; } = "s";

                public Order Order { get; set; }

                public Product Product { get; set; }
            }

            public class Product
            {
                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; } = "p";

                public List<Line> Lines { get; set; } = new List<Line>();
            }

            public class Customer
            {
                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; } = "c";

                public List<Order> Orders { get; set; } = new List<Order>();
            }

            // Order i has two lines, for product i and product i + 1.
            private static Store Build(int orders)
            {
                var store = new Store();
                var products = Enumerable.Range(0, orders + 1).Select(_ => new Product()).ToList();
                for (var i = 0; i < orders; i++)
                {
                    var customer = new Customer();
                    var order = new Order { Customer = customer };
                    customer.Orders.Add(order);
                    foreach (var product in new[] { products[i], products[i + 1] })
                    {
                        var line = new Line { Order = order, Product = product };
                        order.Lines.Add(line);
                        product.Lines.Add(line);
                    }

                    store.Orders.Add(order);
                }

                return store;
            }

            /// <summary>
            /// With 25 orders, the depth-first route is about 150 levels long, and two objects
            /// failed with "nested more than 128 levels deep". Each of them was in fact validated
            /// by another route, so the message was false and the valid store failed.
            /// </summary>
            [Fact]
            public void Valid_store_of_25_orders_passes()
            {
                var (valid, errors) = Run(Build(25));

                Assert.True(valid);
                Assert.Empty(errors);
            }

            [Fact]
            public void Valid_store_of_200_orders_passes()
            {
                var (valid, errors) = Run(Build(200));

                Assert.True(valid);
                Assert.Empty(errors);
            }

            /// <summary>
            /// Every order is found by the store's own list, one level below the list, so its path is
            /// "Orders[i].Code". A depth-first walk reaches most orders through the links between
            /// products and lines, and the path of each one is a long chain of those links.
            /// </summary>
            [Fact]
            public void Each_order_is_reported_once_with_its_shortest_path()
            {
                var store = Build(25);
                foreach (var order in store.Orders) order.Code = null;

                var (valid, errors) = Run(store);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(Enumerable.Range(0, 25).Select(i => $"Orders[{i}].Code | Code is required").ToArray()),
                    errors);
            }

            /// <summary>
            /// The products are reached through the lines of an order: "Orders[0].Lines[0].Product" is
            /// the shortest path to the first one, and the second line of the first order is the shortest
            /// path to the second one. Each product is at most 5 levels deep.
            /// </summary>
            [Fact]
            public void Each_product_is_reported_once_with_its_shortest_path()
            {
                var store = Build(3);
                foreach (var line in store.Orders.SelectMany(o => o.Lines)) line.Product.Name = null;

                var (valid, errors) = Run(store);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect(
                        $"Orders[0].Lines[0].Product.Name | {NameRequired}",
                        $"Orders[0].Lines[1].Product.Name | {NameRequired}",
                        $"Orders[1].Lines[1].Product.Name | {NameRequired}",
                        $"Orders[2].Lines[1].Product.Name | {NameRequired}"),
                    errors);
            }
        }

        /// <summary>
        /// A ring of objects where each one links to the next one and to the one before it. Previous
        /// is declared first, so a depth-first walk starts from the head and goes backwards around
        /// the ring: its route to the object just after the head is the whole ring, 200 levels.
        /// Every object is at most about 100 levels from the head, which is the shortest path.
        /// </summary>
        public class RingOfObjects
        {
            public class RingNode
            {
                public RingNode Previous { get; set; }

                public RingNode Next { get; set; }

                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; } = "ok";
            }

            public class Ring
            {
                public RingNode Head { get; set; }
            }

            private static Ring Build(int count)
            {
                var nodes = Enumerable.Range(0, count).Select(_ => new RingNode()).ToList();
                for (var i = 0; i < count; i++)
                {
                    nodes[i].Next = nodes[(i + 1) % count];
                    nodes[i].Previous = nodes[(i + count - 1) % count];
                }

                return new Ring { Head = nodes[0] };
            }

            [Fact]
            public void Valid_ring_of_200_objects_passes()
            {
                var (valid, errors) = Run(Build(200));

                Assert.True(valid);
                Assert.Empty(errors);
            }

            /// <summary>
            /// The object opposite the head is 100 steps away in both directions. The walk goes
            /// backwards first, so the tie goes to "Previous".
            /// </summary>
            [Fact]
            public void Object_opposite_the_head_is_reported_with_the_shortest_path()
            {
                var ring = Build(200);
                var opposite = ring.Head;
                for (var i = 0; i < 100; i++) opposite = opposite.Next;
                opposite.Name = null;

                var (valid, errors) = Run(ring);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect($"Head.{string.Join(".", Enumerable.Repeat("Previous", 100))}.Name | {NameRequired}"),
                    errors);
            }
        }

        /// <summary>
        /// A chain of 200 objects is 200 levels deep, and no other route is shorter. The ring above
        /// is different, because it has a shortcut. These tests keep the limit for a real chain.
        /// </summary>
        public class AChainThatIsReallyTooDeep
        {
            private static Node Chain(int levels)
            {
                var root = new Node();
                var current = root;
                for (var i = 0; i < levels; i++)
                {
                    current.Next = new Node();
                    current = current.Next;
                }

                return root;
            }

            [Fact]
            public void Object_at_level_129_gets_one_depth_error()
            {
                var (valid, errors) = Run(Chain(129));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{string.Join(".", Enumerable.Repeat("Next", 129))} | {TooDeep}"), errors);
            }

            [Fact]
            public void Chain_of_200_gets_one_depth_error()
            {
                var (valid, errors) = Run(Chain(200));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{string.Join(".", Enumerable.Repeat("Next", 129))} | {TooDeep}"), errors);
            }
        }

        /// <summary>
        /// Rule 2: an object that two routes reach is reported with the shorter one.
        /// </summary>
        public class ObjectsThatTwoRoutesReach
        {
            public class Wrapper
            {
                public Wrapper Inner { get; set; }

                public Node Shared { get; set; }
            }

            public class ShortAndLong
            {
                // The long route is declared first. A depth-first walk takes it first.
                public Wrapper Long { get; set; }

                public Node Short { get; set; }
            }

            public class TwoEqualRoutes
            {
                public Wrapper First { get; set; }

                public Wrapper Second { get; set; }
            }

            public class ListOrProperty
            {
                public List<Node> Items { get; set; }

                public Node Direct { get; set; }
            }

            [Fact]
            public void Shortest_path_wins_over_the_first_path()
            {
                var shared = new Node { Name = null };
                var model = new ShortAndLong
                {
                    Long = new Wrapper { Inner = new Wrapper { Shared = shared } },
                    Short = shared,
                };

                var (valid, errors) = Run(model);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"Short.Name | {NameRequired}"), errors);
            }

            /// <summary>
            /// Both routes are two levels long. The property that is declared first wins, as it did
            /// in a depth-first walk.
            /// </summary>
            [Fact]
            public void Equally_long_routes_report_the_first_property()
            {
                var shared = new Node { Name = null };
                var model = new TwoEqualRoutes
                {
                    First = new Wrapper { Shared = shared },
                    Second = new Wrapper { Shared = shared },
                };

                var (valid, errors) = Run(model);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"First.Shared.Name | {NameRequired}"), errors);
            }

            /// <summary>
            /// "Items[0]" is two levels, one for the property and one for the index, and "Direct" is one.
            /// </summary>
            [Fact]
            public void A_property_is_shorter_than_an_item_of_a_collection()
            {
                var shared = new Node { Name = null };
                var model = new ListOrProperty { Items = new List<Node> { shared }, Direct = shared };

                var (valid, errors) = Run(model);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"Direct.Name | {NameRequired}"), errors);
            }
        }

        /// <summary>
        /// Rule 3: the results are in the order of the walk.
        /// </summary>
        public class ResultOrder
        {
            public class Parent
            {
                public Child A { get; set; }

                public Child B { get; set; }

                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; }
            }

            public class Child
            {
                public Child Deeper { get; set; }

                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; }
            }

            /// <summary>
            /// A depth-first walk reports A.Deeper.Name before B.Name, because it goes down A first.
            /// </summary>
            [Fact]
            public void Shallower_objects_come_before_deeper_objects()
            {
                var model = new Parent
                {
                    A = new Child { Name = null, Deeper = new Child { Name = null } },
                    B = new Child { Name = null },
                    Name = null,
                };

                var errors = RunInOrder(model);

                Assert.Equal(
                    new[]
                    {
                        $"Name | {NameRequired}",
                        $"A.Name | {NameRequired}",
                        $"B.Name | {NameRequired}",
                        $"A.Deeper.Name | {NameRequired}",
                    },
                    errors);
            }

            /// <summary>
            /// An item of a collection that a property holds is two levels below the object: one for
            /// the property and one for the index. It comes after an object that another property
            /// holds directly, which is one level below, even if the collection is declared first.
            /// </summary>
            public class WithItems
            {
                public List<Child> Items { get; set; }

                public Child Direct { get; set; }
            }

            [Fact]
            public void Items_of_a_property_collection_come_after_a_direct_property_of_the_same_object()
            {
                var model = new WithItems
                {
                    Items = new List<Child> { new Child { Name = null } },
                    Direct = new Child { Name = null },
                };

                var errors = RunInOrder(model);

                Assert.Equal(
                    new[]
                    {
                        $"Direct.Name | {NameRequired}",
                        $"Items[0].Name | {NameRequired}",
                    },
                    errors);
            }
        }

        /// <summary>
        /// The depth in a depth error is the length of the shortest path, so the error is never false.
        /// </summary>
        public class DepthErrors
        {
            public class Link
            {
                public Link Next { get; set; }

                public Fork Fork { get; set; }
            }

            public class Fork
            {
                public Branch A { get; set; }

                public Branch B { get; set; }
            }

            public class Branch
            {
                public List<Node> Items { get; set; }

                public Node Direct { get; set; }
            }

            // `links` Link objects in a row, the last one holds the fork, which is one level below it.
            private static Link ChainToFork(int links, Fork fork)
            {
                var root = new Link();
                var current = root;
                for (var i = 1; i < links; i++)
                {
                    current.Next = new Link();
                    current = current.Next;
                }

                current.Fork = fork;
                return root;
            }

            private static string NextPath(int links) =>
                links == 1 ? "" : string.Join(".", Enumerable.Repeat("Next", links - 1)) + ".";

            /// <summary>
            /// The fork is at level 126, and its branches A and B are at level 127. The shared object
            /// is at level 129 from A, which holds it in a list (a property and an index are two
            /// levels), and at level 128 from B, which holds it in a property. Level 128 is within the
            /// limit, so it is validated. A walk that takes the first route it finds, A, would see
            /// level 129 and fail it with "nested more than 128 levels deep", which is false.
            /// </summary>
            [Fact]
            public void An_object_that_a_shorter_route_reaches_within_the_limit_is_validated()
            {
                var shared = new Node { Name = null };
                var fork = new Fork
                {
                    A = new Branch { Items = new List<Node> { shared } },
                    B = new Branch { Direct = shared },
                };

                var (valid, errors) = Run(ChainToFork(126, fork));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(126)}Fork.B.Direct.Name | {NameRequired}"), errors);
            }

            /// <summary>
            /// Two routes reach the same object at level 129, and no route is shorter. Nobody validated
            /// it, so the validator reports it once, and not once for each route.
            /// </summary>
            [Fact]
            public void An_object_that_is_too_deep_by_every_route_gets_one_depth_error()
            {
                var shared = new Node();
                var fork = new Fork
                {
                    A = new Branch { Direct = new Node { Next = shared } },
                    B = new Branch { Direct = new Node { Next = shared } },
                };

                var (valid, errors) = Run(ChainToFork(126, fork));

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"{NextPath(126)}Fork.A.Direct.Next | {TooDeep}"), errors);
            }
        }
    }
}
