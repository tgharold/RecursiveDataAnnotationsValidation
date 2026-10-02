using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
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
    /// 4. An object that Equals an ancestor on its shortest path is validated, and not walked, from
    ///    that path. A longer path that does not pass an equal ancestor still walks it.
    ///
    /// The tests build graphs that stay within the limit of the walk. A depth-first walk that has
    /// the limit of 128, as the walk had before 3.0 added this one, fails them with an error. A
    /// depth-first walk with no limit, such as v2.3.3, overflows the stack on
    /// Valid_store_of_200_orders_passes, which ends the test run.
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
            public async Task Valid_store_of_25_orders_passes_in_the_async_method()
            {
                var results = new List<ValidationResult>();

                var valid = await new RecursiveDataAnnotationValidator()
                    .TryValidateObjectRecursiveAsync(Build(25), results);

                Assert.True(valid);
                Assert.Empty(results);
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
            /// The walk finishes a level for all the parents before it starts the next one. A depth-first
            /// walk reports A.X.Name before B.Name.
            /// </summary>
            [Fact]
            public void A_level_is_finished_for_every_parent_before_the_next_level_starts()
            {
                var model = new Parent
                {
                    A = new Child { Name = null, Deeper = new Child { Name = null } },
                    B = new Child { Name = null, Deeper = new Child { Name = null } },
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
                        $"B.Deeper.Name | {NameRequired}",
                    },
                    errors);
            }

            public class Ordered
            {
                [Required(ErrorMessage = "A is required")]
                public string A { get; set; }

                public Child First { get; set; }

                public List<Child> Items { get; set; }

                public Child Last { get; set; }

                [Required(ErrorMessage = "Z is required")]
                public string Z { get; set; }
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
            public void Root_results_come_first_then_each_level_in_property_order()
            {
                var model = new Ordered
                {
                    First = new Child(),
                    Items = new List<Child> { new Child(), new Child() },
                    Last = new Child(),
                };

                var errors = RunInOrder(model);

                Assert.Equal(
                    new[]
                    {
                        "A | A is required",
                        "Z | Z is required",
                        $"First.Name | {NameRequired}",
                        $"Last.Name | {NameRequired}",
                        $"Items[0].Name | {NameRequired}",
                        $"Items[1].Name | {NameRequired}",
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

        /// <summary>
        /// Rule 4. A computed property can return a new, equal object on each read, so an object that
        /// Equals an object on its own path is validated for its own attributes and not walked (see
        /// the README, "Shared objects, cycles and computed properties"). The path is the shortest
        /// one. Another path to the same object that passes no equal ancestor still walks it, and
        /// the walk does not skip it because the shortest path stopped there.
        /// </summary>
        public class ObjectsThatEqualAnAncestor
        {
            public class Leaf
            {
                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; }
            }

            /// <summary>An entity that Equals another one with the same Id, as an ORM entity does.</summary>
            public class Entity
            {
                public int Id { get; set; }

                public Entity Copy { get; set; }

                public Leaf Child { get; set; }

                public override bool Equals(object obj) => obj is Entity other && other.Id == Id;

                public override int GetHashCode() => Id;
            }

            public class Wrapper2
            {
                public Wrapper3 Inner { get; set; }
            }

            public class Wrapper3
            {
                public Entity X { get; set; }
            }

            public class LongFirst
            {
                public Wrapper2 Long { get; set; }

                public Entity Short { get; set; }
            }

            public class ShortFirst
            {
                public Entity Short { get; set; }

                public Wrapper2 Long { get; set; }
            }

            // `x` has the same Id as `a`, and `a` holds `x` as its Copy, so the shortest path to `x`
            // (Short.Copy, 2 levels) passes an equal ancestor. A second path to `x` (Long.Inner.X,
            // 3 levels) passes none, and `x` holds an invalid object.
            private static (Entity A, Entity X, Wrapper2 Long) Build()
            {
                var x = new Entity { Id = 1, Child = new Leaf { Name = null } };
                var a = new Entity { Id = 1, Copy = x };
                var path = new Wrapper2 { Inner = new Wrapper3 { X = x } };
                return (a, x, path);
            }

            /// <summary>
            /// v2.3.3 walked the first path in property order, which is Long, and found the invalid
            /// Leaf. It must still be found, although the walk takes Short first.
            /// </summary>
            [Fact]
            public void A_longer_path_that_passes_no_equal_ancestor_walks_the_object()
            {
                var (a, _, path) = Build();

                var (valid, errors) = Run(new LongFirst { Long = path, Short = a });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"Long.Inner.X.Child.Name | {NameRequired}"), errors);
            }

            /// <summary>
            /// The same graph with the properties in the other order. v2.3.3 took Short first, stopped
            /// at `x` there, and then dropped the second path, so it passed. That was a known gap.
            /// </summary>
            [Fact]
            public void The_property_order_does_not_decide()
            {
                var (a, _, path) = Build();

                var (valid, errors) = Run(new ShortFirst { Short = a, Long = path });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"Long.Inner.X.Child.Name | {NameRequired}"), errors);
            }

            public class NamedEntity
            {
                public int Id { get; set; }

                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; } = "ok";

                public NamedEntity First { get; set; }

                public NamedEntity Second { get; set; }

                public override bool Equals(object obj) => obj is NamedEntity other && other.Id == Id;

                public override int GetHashCode() => Id;
            }

            /// <summary>
            /// Two paths stop at the same object. Its own attributes are validated once, and not once
            /// for each path.
            /// </summary>
            [Fact]
            public void An_object_that_two_paths_stop_at_is_validated_once()
            {
                var copy = new NamedEntity { Id = 1, Name = null };
                var root = new NamedEntity { Id = 1, First = copy, Second = copy };

                var (valid, errors) = Run(root);

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"First.Name | {NameRequired}"), errors);
            }

            public class EqChain
            {
                public int Id { get; set; }

                public EqChain Next { get; set; }

                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; } = "ok";

                public override bool Equals(object obj) => obj is EqChain other && other.Id == Id;

                public override int GetHashCode() => Id;
            }

            /// <summary>
            /// The check for an equal ancestor comes before the check for the depth. The object at
            /// level 129 Equals the object at level 5, so it is validated and not walked, and it gets
            /// its own error and no depth error.
            /// </summary>
            [Fact]
            public void An_object_that_equals_an_ancestor_at_level_129_is_validated_and_has_no_depth_error()
            {
                var root = new EqChain { Id = 0 };
                var current = root;
                for (var id = 1; id <= 128; id++)
                {
                    current.Next = new EqChain { Id = id };
                    current = current.Next;
                }

                current.Next = new EqChain { Id = 5, Name = null };

                var (valid, errors) = Run(root);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect($"{string.Join(".", Enumerable.Repeat("Next", 129))}.Name | {NameRequired}"),
                    errors);
            }

            public class RegionStore
            {
                public List<Region> Regions { get; set; }

                public List<RegionOrder> Orders { get; set; }
            }

            public class Region
            {
                public List<RegionCustomer> Customers { get; set; }
            }

            public class RegionCustomer
            {
                public List<RegionOrder> Orders { get; set; }
            }

            public class RegionOrder
            {
                public int Id { get; set; }

                public RegionCustomer Customer { get; set; }

                public List<RegionLine> Lines { get; set; }

                public override bool Equals(object obj) => obj is RegionOrder other && other.Id == Id;

                public override int GetHashCode() => Id;
            }

            public class RegionLine
            {
                [Required(ErrorMessage = "Sku is required")]
                public string Sku { get; set; }
            }

            /// <summary>
            /// Known limit. An ORM that loads without tracking can return two instances of order 7.
            /// Here `copy` is only reachable through customer `c`, and the shortest path to `c` is
            /// Orders[0].Customer, below the tracked order 7. The copy is the order that `c` holds,
            /// so it Equals an ancestor on that path, and the walk stops there. The customer is
            /// reached by another path, Regions[0].Customers[0], which has no equal ancestor, but
            /// the customer was queued on its shortest path and is not walked a second time. To find
            /// the line, the walk would have to walk a shared object once for each different chain of
            /// equal ancestors, and that number has no bound. v2.3.3 found it, by the first path in
            /// property order.
            /// </summary>
            [Fact(Skip = "Known limit. A shared object is walked once, by its shortest path, and an equal ancestor on that path stops the walk.")]
            public void An_object_below_a_shared_customer_is_validated_when_the_shortest_path_has_an_equal_ancestor()
            {
                var customer = new RegionCustomer();
                var tracked = new RegionOrder { Id = 7, Customer = customer };
                var copy = new RegionOrder
                {
                    Id = 7,
                    Customer = customer,
                    Lines = new List<RegionLine> { new RegionLine { Sku = null } },
                };
                customer.Orders = new List<RegionOrder> { copy };
                var store = new RegionStore
                {
                    Regions = new List<Region> { new Region { Customers = new List<RegionCustomer> { customer } } },
                    Orders = new List<RegionOrder> { tracked },
                };

                var (valid, errors) = Run(store);

                Assert.False(valid);
                Assert.Equal(
                    ResultText.Expect("Regions[0].Customers[0].Orders[0].Lines[0].Sku | Sku is required"),
                    errors);
            }
        }

        /// <summary>
        /// A collection that a property holds is enumerated when the walk reaches it, which is after
        /// the objects at its own level and above are validated. It is not enumerated when the
        /// property is read. Up to 2.3, the walk enumerated it at once, while it validated the
        /// object that holds it. So a Validate method that changes a collection is now seen by the
        /// walk, and a Validate method that changes the list that holds it no longer makes
        /// enumeration throw. Changing a collection during validation is unusual, and the tests pin
        /// the order so that a change to it is on purpose.
        /// See: https://learn.microsoft.com/dotnet/api/system.invalidoperationexception
        /// (a List&lt;T&gt; that changes during a foreach throws it).
        /// </summary>
        public class WhenACollectionIsEnumerated
        {
            public class HasItems
            {
                public List<Node> Items { get; set; } = new List<Node> { new Node() };
            }

            /// <summary>Adds an invalid item to the collection of another object, when it is validated.</summary>
            public class Mutator : IValidatableObject
            {
                public HasItems Target { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    Target.Items.Add(new Node { Name = null });
                    return Enumerable.Empty<ValidationResult>();
                }
            }

            public class MutateRoot
            {
                public HasItems A { get; set; }

                public Mutator B { get; set; }
            }

            /// <summary>
            /// B is validated before the items of A are enumerated, so the item that B adds is validated.
            /// Up to 2.3, A.Items was enumerated first, and the item that B added was never seen.
            /// </summary>
            [Fact]
            public void A_change_that_a_sibling_makes_during_validation_is_seen()
            {
                var a = new HasItems();

                var (valid, errors) = Run(new MutateRoot { A = a, B = new Mutator { Target = a } });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"A.Items[1].Name | {NameRequired}"), errors);
            }

            /// <summary>An item that adds a second item to the list that holds it, when it is validated.</summary>
            public class SelfAdder : IValidatableObject
            {
                [Required(ErrorMessage = NameRequired)]
                public string Name { get; set; } = "ok";

                public List<SelfAdder> Owner { get; set; }

                public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
                {
                    if (Owner != null && Owner.Count == 1)
                    {
                        Owner.Add(new SelfAdder { Name = null });
                    }

                    return Enumerable.Empty<ValidationResult>();
                }
            }

            public class SelfAdderRoot
            {
                public List<SelfAdder> Items { get; set; }
            }

            /// <summary>
            /// Up to 2.3, the walk was inside the foreach over Items when the item added to the
            /// list, so the next step of the foreach threw "Collection was modified". The list of
            /// Items is now enumerated completely before the item is validated, and the item's own
            /// Owner property, which is the same list, is enumerated after it.
            /// </summary>
            [Fact]
            public void An_item_that_adds_to_the_list_that_holds_it_does_not_make_enumeration_throw()
            {
                var item = new SelfAdder();
                var list = new List<SelfAdder> { item };
                item.Owner = list;

                var (valid, errors) = Run(new SelfAdderRoot { Items = list });

                Assert.False(valid);
                Assert.Equal(ResultText.Expect($"Items[0].Owner[1].Name | {NameRequired}"), errors);
            }
        }
    }
}
