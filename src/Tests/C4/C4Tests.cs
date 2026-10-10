public class C4Tests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            C4Context
                title System Context diagram
                Person(user, "User", "A user of the system")
                System(system, "System", "The main system")
                Rel(user, system, "Uses")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task External()
    {
        const string input =
            """
            C4Context
                title Banking System Context
                Person(customer, "Banking Customer", "A customer of the bank")
                System(banking, "Internet Banking", "Allows customers to manage accounts")
                System_Ext(email, "E-mail System", "External email provider")
                Rel(customer, banking, "Views account info")
                Rel(banking, email, "Sends emails", "SMTP")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Container()
    {
        const string input =
            """
            C4Container
                title Container diagram for Banking System
                Person(customer, "Customer", "Bank customer")
                Container(web, "Web Application", "React", "Provides banking UI")
                Container(api, "API Server", "Node.js", "Handles requests")
                ContainerDb(db, "Database", "PostgreSQL", "Stores user data")
                Rel(customer, web, "Uses", "HTTPS")
                Rel(web, api, "Calls", "JSON/HTTPS")
                Rel(api, db, "Reads/Writes", "SQL")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Component()
    {
        const string input =
            """
            C4Component
                title Component diagram for API
                Component(auth, "Auth Controller", "Express", "Handles authentication")
                Component(user, "User Controller", "Express", "Manages users")
                Component(service, "User Service", "TypeScript", "Business logic")
                Rel(auth, service, "Uses")
                Rel(user, service, "Uses")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MixedElements()
    {
        const string input =
            """
            C4Context
                title E-commerce Platform
                Person(buyer, "Buyer", "Online shopper")
                Person(seller, "Seller", "Product vendor")
                System(platform, "E-commerce Platform", "Online marketplace")
                System_Ext(payment, "Payment Gateway", "Processes payments")
                System_Ext(shipping, "Shipping Service", "Handles delivery")
                Rel(buyer, platform, "Browses and buys")
                Rel(seller, platform, "Lists products")
                Rel(platform, payment, "Processes payments")
                Rel(platform, shipping, "Ships orders")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NoRelationships()
    {
        const string input =
            """
            C4Context
                title Standalone Systems
                System(a, "System A", "First system")
                System(b, "System B", "Second system")
                System(c, "System C", "Third system")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            C4Context
                title Enterprise Architecture Overview

                Person(admin, "Administrator", "System administrator with full access")
                Person(user, "Regular User", "End user of the application")

                System(core, "Core System", "Main application server")
                System(auth, "Auth Service", "Authentication and authorization")
                System(db, "Database", "PostgreSQL database cluster")

                System_Ext(payment, "Payment Gateway", "Third-party payment processor")
                System_Ext(email, "Email Service", "SendGrid email delivery")
                System_Ext(cdn, "CDN", "Content delivery network")

                Rel(admin, core, "Manages", "HTTPS")
                Rel(user, core, "Uses", "HTTPS")
                Rel(core, auth, "Authenticates via")
                Rel(core, db, "Reads/Writes", "TCP/5432")
                Rel(core, payment, "Processes payments", "HTTPS")
                Rel(core, email, "Sends notifications", "SMTP")
                Rel(core, cdn, "Serves assets", "HTTPS")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Boundaries()
    {
        const string input =
            """
            C4Container
                title Internet Banking System

                Person(customer, "Customer", "A bank customer")

                System_Boundary(banking, "Internet Banking") {
                    Container(web, "Web App", "React", "Delivers content to the customer")
                    Container(api, "API", "Node.js", "Handles business logic")
                    ContainerDb(db, "Database", "PostgreSQL", "Stores user accounts")
                }

                System_Ext(email, "Email System", "Sends email to customers")

                Rel(customer, web, "Uses", "HTTPS")
                Rel(web, api, "Calls", "JSON/HTTPS")
                Rel(api, db, "Reads/Writes", "SQL")
                Rel(api, email, "Sends email using", "SMTP")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NestedBoundaries()
    {
        const string input =
            """
            C4Container
                title Internet Banking - Nested Boundaries

                Person(customer, "Customer", "A bank customer")

                System_Boundary(banking, "Internet Banking") {
                    Container_Boundary(apiapp, "API Application") {
                        Container(controller, "Controller", "MVC", "Handles requests")
                        Container(service, "Service", "Spring", "Business logic")
                    }
                    ContainerDb(db, "Database", "Oracle", "Stores accounts")
                }

                Rel(customer, controller, "Makes requests", "HTTPS")
                Rel(controller, service, "Uses")
                Rel(service, db, "Reads/Writes", "JDBC")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task RelationshipTechnology()
    {
        const string input =
            """
            C4Context
                title Relationship Technology
                Person(user, "User", "A user of the system")
                System(sys, "System", "The main system")
                System_Ext(email, "Email System", "Delivers email")
                Rel(user, sys, "Uses", "HTTPS")
                Rel(sys, email, "Sends notifications", "SMTP")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BoundaryEdgeRouting()
    {
        // The a -> c relationship skips the middle element in the same boundary
        // row, so a straight line would cross the "B" box.
        const string input =
            """
            C4Container
                title Boundary Edge Routing
                System_Boundary(b, "System") {
                    Container(a, "A", "tech", "first")
                    Container(mid, "B", "tech", "second")
                    Container(c, "C", "tech", "third")
                }
                Rel(a, mid, "to b")
                Rel(mid, c, "to c")
                Rel(a, c, "skips b")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task WideSideLabel()
    {
        // The user -> api relationship is a long edge routed down the right side
        // with a wide label; the canvas must widen so its chip is not clipped.
        const string input =
            """
            C4Context
                title Wide Side Label
                Person(user, "User", "A user")
                System(web, "Web App", "Frontend")
                System(api, "API", "Backend")
                Rel(user, web, "Uses", "HTTPS")
                Rel(web, api, "Calls", "REST")
                Rel(user, api, "Receives push notifications from", "WebSocket")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DirectionalRelationships()
    {
        // Each suffix pins the target relative to the core: up above, down below,
        // left and right on the same rank to either side.
        const string input =
            """
            C4Context
                title Directional Relationships
                System(core, "Core", "Central system")
                System(up, "Upstream", "Above the core")
                System(down, "Downstream", "Below the core")
                System(left, "Left Service", "Left of the core")
                System(right, "Right Service", "Right of the core")
                Rel_U(core, up, "Publishes to")
                Rel_D(core, down, "Calls")
                Rel_L(core, left, "Reads from")
                Rel_R(core, right, "Writes to")
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BackRelationship()
    {
        // Rel_Back draws the arrowhead at the source end (here, back up at "client").
        const string input =
            """
            C4Context
                title Back Relationship
                System(client, "Client", "Sends requests")
                System(server, "Server", "Returns responses")
                Rel_Back(client, server, "Polls for updates", "HTTP")
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task DirectionalLabelsDoNotOverlapNodes()
    {
        const string input =
            """
            C4Context
                title Directional Relationships
                System(core, "Core", "Central system")
                System(up, "Upstream", "Above the core")
                System(down, "Downstream", "Below the core")
                System(left, "Left Service", "Left of the core")
                System(right, "Right Service", "Right of the core")
                Rel_U(core, up, "Publishes to")
                Rel_D(core, down, "Calls")
                Rel_L(core, left, "Reads from")
                Rel_R(core, right, "Writes to")
            """;

        var rects = ParseRects(Mermaid.Render(input));

        // System boxes are filled with the C4 system colour; label chips are white.
        var nodeBoxes = rects.Where(_ => _.Fill == "#1168BD").ToList();
        var labelChips = rects.Where(_ => _.Fill == "#FFFFFF").ToList();

        await Assert.That(nodeBoxes.Count).IsEqualTo(5).Because("expected five system boxes");
        await Assert.That(labelChips).IsNotEmpty().Because("expected relationship label chips");

        // Boxes and chips share the same coordinate space (one body group), so a
        // raw rectangle-intersection test is valid.
        foreach (var chip in labelChips)
        {
            foreach (var box in nodeBoxes)
            {
                await Assert.That(Intersects(chip, box))
                    .IsFalse()
                    .Because($"label chip at ({chip.X},{chip.Y}) {chip.W}x{chip.H} overlaps a node box at ({box.X},{box.Y})");
            }
        }
    }

    [Test]
    public async Task NeighborLabelDoesNotOverlapInterveningNode()
    {
        // a and c are neighbours; b is declared between them. Without keeping the
        // neighbour group contiguous, b would land between a and c and the wide
        // "Exchanges data with" label would be drawn across it.
        const string input =
            """
            C4Context
                title Neighbor Adjacency
                System(a, "Service A", "first")
                System(b, "Service B", "middle")
                System(c, "Service C", "third")
                Rel_Neighbor(a, c, "Exchanges data with")
            """;

        var rects = ParseRects(Mermaid.Render(input));
        var nodeBoxes = rects.Where(_ => _.Fill == "#1168BD").ToList();
        var labelChips = rects.Where(_ => _.Fill == "#FFFFFF").ToList();

        await Assert.That(nodeBoxes.Count).IsEqualTo(3).Because("expected three system boxes");
        await Assert.That(labelChips).IsNotEmpty().Because("expected the neighbour label chip");

        foreach (var chip in labelChips)
        {
            foreach (var box in nodeBoxes)
            {
                await Assert.That(Intersects(chip, box))
                    .IsFalse()
                    .Because($"label chip at ({chip.X},{chip.Y}) {chip.W}x{chip.H} overlaps a node box at ({box.X},{box.Y})");
            }
        }
    }

    [Test]
    public async Task RoutedLabelDoesNotOverlapNodes()
    {
        // user -> api is a long edge whose wide label rides the routed mid-point,
        // which would otherwise land on the Web App box on the same rank.
        const string input =
            """
            C4Context
                title Wide Side Label
                Person(user, "User", "A user")
                System(web, "Web App", "Frontend")
                System(api, "API", "Backend")
                Rel(user, web, "Uses", "HTTPS")
                Rel(web, api, "Calls", "REST")
                Rel(user, api, "Receives push notifications from", "WebSocket")
            """;

        var rects = ParseRects(Mermaid.Render(input));

        // Person bodies and system boxes are the node shapes; chips are white.
        var nodeBoxes = rects.Where(_ => _.Fill is "#1168BD" or "#08427B").ToList();
        var labelChips = rects.Where(_ => _.Fill == "#FFFFFF").ToList();

        await Assert.That(nodeBoxes.Count).IsEqualTo(3).Because("expected two systems and one person body");
        await Assert.That(labelChips).IsNotEmpty().Because("expected relationship label chips");

        foreach (var chip in labelChips)
        {
            foreach (var box in nodeBoxes)
            {
                await Assert.That(Intersects(chip, box))
                    .IsFalse()
                    .Because($"label chip at ({chip.X},{chip.Y}) {chip.W}x{chip.H} overlaps a node box at ({box.X},{box.Y})");
            }
        }
    }

    [Test]
    public Task DuplicateElementIds()
    {
        // Two containers reuse the id "web" inside a boundary. The duplicate must
        // not crash the id lookup maps; the relationship resolves to the first.
        const string input =
            """
            C4Container
                title Duplicate Element Ids
                Person(customer, "Customer", "A bank customer")
                System_Boundary(banking, "Internet Banking") {
                    Container(web, "Web App", "React", "Delivers content to the customer")
                    Container(web, "Duplicate", "React", "Reuses the web id")
                }
                Rel(customer, web, "Uses", "HTTPS")
            """;

        return VerifySvg(input);
    }

    // A plain Boundary with a type, queue and database elements at every level, a two-way relationship,
    // the long spellings of the directional ones, and the tag and styling statements that are read but
    // not drawn.
    [Test]
    public Task BoundariesQueuesAndTwoWayRelationships()
    {
        const string input =
            """
            C4Context
                title Order handling
                Person(customer, "Customer", $descr="Places orders", $tags="v1")
                Boundary(shop, "Web shop", "Product team") {
                    System(storefront, "Storefront", "Takes the orders")
                    SystemQueue(orders, "Order queue", "Holds orders until they are picked up")
                    ComponentDb(cache, "Basket cache", "Redis", "Keeps open baskets")
                }
                SystemQueue_Ext(courier, "Courier feed")
                BiRel(customer, storefront, "Browses and buys")
                Rel_Right(storefront, orders, "Queues")
                Rel_Down(storefront, cache, "Reads", "RESP")
                Rel(storefront, courier, "Hands over", $techn="AMQP")
                UpdateElementStyle(customer, $fontColor="red", $bgColor="grey", $borderColor="red")
                UpdateRelStyle(customer, storefront, $textColor="blue", $offsetY="-40")
                UpdateBoundaryStyle(shop, $borderColor="blue")
                UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
            """;

        return VerifySvg(input);
    }

    // A dynamic diagram numbers its steps with RelIndex, and deployment nodes name their own type.
    [Test]
    public Task DynamicWithDeploymentNodes()
    {
        const string input =
            """
            C4Dynamic
                title Sign in
                Deployment_Node(browser, "Customer's computer", "Windows or macOS") {
                    Container(spa, "Single-Page App", "JavaScript", "The banking front end")
                }
                Node(dc, "Data centre") {
                    Container(api, "API", "", "Serves the front end")
                    ContainerDb(db, "Database", "PostgreSQL")
                }
                RelIndex(1, spa, api, "Submits credentials", "JSON/HTTPS")
                RelIndex(2, api, db, "Looks up")
            """;

        return VerifySvg(input);
    }

    static bool Intersects(SvgRect a, SvgRect b)
    {
        const double tolerance = 1.0;
        return a.X < b.X + b.W - tolerance &&
               b.X < a.X + a.W - tolerance &&
               a.Y < b.Y + b.H - tolerance &&
               b.Y < a.Y + a.H - tolerance;
    }

    static List<SvgRect> ParseRects(string svg)
    {
        XNamespace ns = "http://www.w3.org/2000/svg";
        return XDocument.Parse(svg)
            .Descendants(ns + "rect")
            .Where(_ =>
                _.Attribute("x") is not null &&
                _.Attribute("y") is not null &&
                _.Attribute("width") is not null &&
                _.Attribute("height") is not null)
            .Select(_ => new SvgRect(
                (double)_.Attribute("x")!,
                (double)_.Attribute("y")!,
                (double)_.Attribute("width")!,
                (double)_.Attribute("height")!,
                (string?)_.Attribute("fill")))
            .ToList();
    }

    record SvgRect(double X, double Y, double W, double H, string? Fill);
}