using Naiad.Diagrams.UseCase;

public class UseCaseTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            usecase-beta
            direction LR
            actor Customer("Customer")
            systemBoundary "Order system"
              Checkout("Place order")
            end
            Customer --> Checkout
            """;

        return VerifySvg(input);
    }

    // The example Mermaid's documentation draws with its defaults and with the classic look.
    [Test]
    public Task TwoBoundaries()
    {
        const string input =
            """
            usecase-beta
            direction LR
            actor Customer
            actor Support
            systemBoundary Storefront
              Browse("Browse catalogue")
              Checkout("Checkout")
            end
            systemBoundary Fulfilment
              Track("Track delivery")
            end
            Customer --> Browse
            Customer --> Checkout
            Customer --> Track
            Support --> Track
            Checkout ..> : include Browse
            """;

        return VerifySvg(input);
    }

    // Relationships before declarations, a rectangle use case, a quoted declaration and an inferred use case.
    [Test]
    public Task ActorsAndUseCases()
    {
        const string input =
            """
            usecase-beta
            Customer --> Login
            actor Customer
            actor Admin("Main administrator")
            Login("Sign in")
            Report[Generate report]
            "Reset password"
            Admin --> Report
            Customer --> Reset_password
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ActorVariants()
    {
        const string input =
            """
            usecase-beta
            actor Normal("Normal actor")
            actor Hollow("Hollow actor")@{ type: hollow }
            actor Awesome("Awesome actor")@{ type: awesome }
            actor Fallback("Missing icon fallback")@{ icon: "not-registered:user" }
            Normal --> Manage
            Hollow --> Manage
            Awesome --> Manage
            Fallback --> Manage
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task BusinessAndStereotypes()
    {
        const string input =
            """
            usecase-beta
            actor SalesAgent("Sales agent")@{ business: true } <<Employee>>
            actor Broker@{ type: hollow, business: true }
            Quote("Prepare quote")@{ business: true } <<Core>>
            Archive[Archive quote] <<Record>>
            SalesAgent --> Quote
            Broker --> Quote
            Quote --> Archive
            """;

        return VerifySvg(input);
    }

    // The seven association operators, each keeping its marker and the end it is on.
    [Test]
    public Task Associations()
    {
        const string input =
            """
            usecase-beta
            actor User
            actor Support
            Start
            Finish
            User --> Start
            Start <-- Support
            Start -- Finish
            User --o Start
            Start o-- Support
            User --x Finish
            Finish x-- Support
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task IncludeExtendGeneralization()
    {
        const string input =
            """
            usecase-beta
            actor Admin
            actor Person
            Checkout
            Payment
            ApplyCoupon
            Admin --|> Person
            Checkout ..> : include Payment
            ApplyCoupon ..> : extend Checkout
            ApplyCoupon --|> Checkout
            """;

        return VerifySvg(input);
    }

    // Unquoted, quoted and markdown labels, on nodes and on edges, with entity codes and a longer edge.
    [Test]
    public Task Labels()
    {
        const string input =
            """
            usecase-beta
            actor Reviewer("`*Reviewer*`")
            Literal(**Literal markers**)
            Formatted("`**Formatted label**
            with a physical line break`")
            Quoted("Show #quot;quoted#quot; text")
            Priced[Costs 19.95 EUR — VAT included!]
            Reviewer -- "`opens **form**`" --> Formatted
            Reviewer -- files R&D report --> Literal
            Reviewer --> Quoted
            Reviewer -- "longer" ----> Priced
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Notes()
    {
        const string input =
            """
            usecase-beta
            note for Login "`Requires an **active session**`"
            note for User "Starts the workflow"
            actor User
            Login("Sign in")
            User --> Login
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task JsonTable()
    {
        const string input =
            """
            usecase-beta
            Inspect("Inspect payload")
            json Payload@{
              "2": "second in source",
              "1": "first after 2",
              "enabled": true,
              "count": 3,
              "missing": null,
              "colors": ["Red", "Green"],
              "address": { "city": "Oslo" },
              "items": [{ "name": "Book" }],
              "emptyObject": {},
              "emptyArray": []
            }:::data
            Inspect --> Payload
            classDef data stroke:#3572a5
            style Payload stroke-width:2px
            """;

        return VerifySvg(input);
    }

    // Notes were measured before JSON tables, so a note on a table was left out while the line tying it to
    // the table was still added, and drawing that line threw.
    [Test]
    public Task NoteOnJsonTable()
    {
        const string input =
            """
            usecase-beta
            json Payload@{ "id": 1, "name": "Book" }
            note for Payload "Sent by the client"
            """;

        return VerifySvg(input);
    }

    // The layout kept the boundary and the actor under one id, so the actor was placed on the boundary's
    // corner and its edge looped round the use case.
    [Test]
    public Task ActorSharingABoundaryId()
    {
        const string input =
            """
            usecase-beta
            actor A
            systemBoundary A
              B("Book")
            end
            A --> B
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Styling()
    {
        const string input =
            """
            usecase-beta
            actor Customer:::external
            Checkout("Checkout"):::critical
            systemBoundary Account
              Profile[Edit profile]
            end
            Customer opens@-- "starts checkout" ---> Checkout
            Checkout --> Profile
            classDef default stroke:#7f8ea3
            classDef external,critical stroke-width:3px
            class Customer,Checkout external,critical
            style Checkout stroke:#c33,stroke-width:4px,fill:#fde
            style Account stroke:#536878
            style opens stroke:#06c,stroke-width:3px
            opens@{ animation: fast }
            """;

        return VerifySvg(input);
    }

    // A package boundary, set inline and by a later metadata statement aimed at a quoted boundary.
    [Test]
    public Task PackageBoundaries()
    {
        const string input =
            """
            usecase-beta
            systemBoundary sb1["Payment service"]@{ type: package }
              actor Clerk("Payment clerk")
              Authorize("Authorize payment")
              Receipt[Create receipt]
            end
            systemBoundary "Reporting"
              Report("Monthly report")
            end
            Reporting@{ type: package }
            Clerk --> Authorize
            Authorize --> Receipt
            Receipt --> Report
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task TopDown()
    {
        const string input =
            """
            usecase-beta
            direction TB
            actor Customer
            actor Clerk
            systemBoundary Shop
              Browse("Browse catalogue")
              Order("Place order")
              Pay("Pay")
            end
            Customer --> Browse
            Customer --> Order
            Order ..> : include Pay
            Clerk --> Order
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task FrontMatterTitle()
    {
        const string input =
            """
            ---
            title: Account access
            ---
            usecase-beta
            accTitle: Account access use cases
            accDescr {
              A customer signs in and can reset a password.
            }
            %% Actors are declared explicitly.
            actor Customer

            SignIn("Sign in")
            Reset("Reset password")
            Customer --> SignIn
            Customer --> Reset
            """;

        return VerifySvg(input);
    }

    // The complete example from Mermaid's documentation.
    [Test]
    public Task CompleteExample()
    {
        const string input =
            """
            usecase-beta
            direction LR
            accTitle: Online ordering use cases
            accDescr {
              A customer places an order through the storefront.
              Staff review the order and inspect its data.
            }
            actor Customer("Customer")
            actor Staff("Order staff")@{ type: hollow, business: true } <<Employee>>
            systemBoundary ordering["Ordering System"]@{ type: package }:::system
              Browse("Browse products")
              Checkout("Checkout") <<Core>>:::critical
              Payment("Process payment")
              Review[Review order]
            end
            json OrderData@{
              "status": "pending",
              "items": [{ "name": "Book", "quantity": 1 }],
              "total": 29.95
            }:::data
            note for Checkout "`Validates the **cart** before payment`"
            Customer starts@-- "places order" ---> Checkout
            Customer --> Browse
            Checkout pays@..> : include Payment
            Staff --> Review
            Review --> OrderData
            classDef system stroke:#c8a02a,stroke-width:2px
            classDef critical stroke:#c33,stroke-width:3px
            classDef data stroke:#3572a5
            starts@{ animation: fast }
            style pays stroke:#6b46c1,stroke-width:2px
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Empty()
    {
        const string input =
            """
            usecase-beta
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task Parse_ForwardReferencesAndInference()
    {
        var model = Parse(
            """
            usecase-beta
            Customer --> Login
            actor Customer
            Login("Sign in")
            Report[Generate report]
            "Reset password"
            Customer --> Unknown
            """);

        await Assert.That(model.Direction).IsEqualTo(Direction.LeftToRight);
        await Assert.That(model.Actors.Select(_ => _.Id)).IsEquivalentTo(["Customer"]);
        await Assert.That(model.UseCases.Select(_ => $"{_.Id}:{_.Label}:{_.Shape}"))
            .IsEquivalentTo(["Login:Sign in:Ellipse", "Report:Generate report:Rectangle", "Reset_password:Reset password:Ellipse", "Unknown:Unknown:Ellipse"]);
        await Assert.That(model.Relationships.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_Operators()
    {
        var model = Parse(
            """
            usecase-beta
            A --> B
            A <-- B
            A -- B
            A --o B
            A o-- B
            A --x B
            A x-- B
            A ----> B
            A --|> B
            A ..> : include B
            A ..> : extend B
            A -- "label" ---> B
            A <-- text -- B
            """);

        var described = model.Relationships
            .Select(_ => $"{_.Type} {_.StartMarker}-{_.EndMarker} {_.MinLength} {_.Label}".TrimEnd())
            .ToList();
        await Assert.That(described).IsEquivalentTo(
            [
                "Association None-Arrow 1",
                "Association Arrow-None 1",
                "Association None-None 1",
                "Association None-Circle 1",
                "Association Circle-None 1",
                "Association None-Cross 1",
                "Association Cross-None 1",
                "Association None-Arrow 3",
                "Generalization None-Extension 1",
                "Include None-Arrow 1",
                "Extend None-Arrow 1",
                "Association None-Arrow 2 label",
                "Association Arrow-None 1 text"
            ],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task Parse_ActorMetadataAndStereotype()
    {
        var model = Parse(
            """
            usecase-beta
            actor SalesAgent("Sales agent")@{ business: true } <<Employee>>:::staff
            actor Broker@{
              type: hollow
              business: true
            }
            actor Icon@{ icon: "fa:user" }
            actor A, B
            """);

        var agent = model.Actors[0];
        await Assert.That(agent.Label).IsEqualTo("Sales agent");
        await Assert.That(agent.Business).IsTrue();
        await Assert.That(agent.Stereotype).IsEqualTo("Employee");
        await Assert.That(agent.Classes).IsEquivalentTo(["staff"]);
        await Assert.That(model.Actors[1].Type).IsEqualTo(ActorType.Hollow);
        await Assert.That(model.Actors[1].Business).IsTrue();
        await Assert.That(model.Actors[2].Type).IsEqualTo(ActorType.Icon);
        await Assert.That(model.Actors[2].Icon).IsEqualTo("fa:user");
        await Assert.That(model.Actors.Select(_ => _.Id)).IsEquivalentTo(["SalesAgent", "Broker", "Icon", "A", "B"]);
    }

    [Test]
    public async Task Parse_BoundariesAndLaterMetadata()
    {
        var model = Parse(
            """
            usecase-beta
            systemBoundary "Payment service"
              actor Clerk("Payment clerk")
              %% a comment inside the boundary
              Authorize("Authorize payment")

            end
            Payment_service@{ type: package }
            Clerk --> Authorize
            """);

        var boundary = model.Boundaries.Single();
        await Assert.That(boundary.Id).IsEqualTo("Payment_service");
        await Assert.That(boundary.Title).IsEqualTo("Payment service");
        await Assert.That(boundary.Type).IsEqualTo(BoundaryType.Package);
        await Assert.That(boundary.MemberIds).IsEquivalentTo(["Clerk", "Authorize"]);
        await Assert.That(model.UseCases.Select(_ => _.Id)).IsEquivalentTo(["Authorize"]);
    }

    // A boundary opened twice under one id was two boundaries, which the layout then took as one box and
    // drew the second as an empty box at the origin.
    [Test]
    public async Task Parse_BoundaryOpenedAgainAddsToTheFirst()
    {
        var model = Parse(
            """
            usecase-beta
            systemBoundary S
              A
            end
            systemBoundary S["Shop"]
              B
            end
            """);

        var boundary = model.Boundaries.Single();
        await Assert.That(boundary.Title).IsEqualTo("Shop");
        await Assert.That(boundary.MemberIds).IsEquivalentTo(["A", "B"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task Parse_NotesAndJson()
    {
        var model = Parse(
            """
            usecase-beta
            note for Login "Requires a session"
            json Data@{ "b": 1, "a": { "c": [true, null] }, "b": 2, "e": {} }
            """);

        var note = model.Notes.Single();
        await Assert.That(note.TargetId).IsEqualTo("Login");
        await Assert.That(note.Text).IsEqualTo("Requires a session");
        await Assert.That(model.UseCases.Select(_ => _.Id)).IsEquivalentTo(["Login"]);
        await Assert.That(model.JsonTables.Single().Rows.Select(_ => $"{_.Key}={_.Value}"))
            .IsEquivalentTo(["b=2", "a.c[0]=true", "a.c[1]=null", "e={}"], CollectionOrdering.Matching);
    }

    // A numeric entity naming a surrogate is no character, and turning it into one threw.
    [Test]
    public async Task Parse_SurrogateEntityIsKeptAsWritten()
    {
        var model = Parse(
            """
            usecase-beta
            A("a#55296;b #65;")
            """);

        await Assert.That(model.UseCases.Single().Label).IsEqualTo("a#55296;b A");
    }

    // A quoted name's id is made from its label. Every character outside ASCII used to become `_`, so two
    // labels of the same length in another script got the same id and merged into one element.
    [Test]
    public async Task Parse_LabelsInAnotherScriptStayApart()
    {
        var model = Parse(
            """
            usecase-beta
            actor "日本"
            actor "中国"
            "日本" --> "登录"
            "中国" --> "注册"
            """);

        await Assert.That(model.Actors.Select(_ => _.Label)).IsEquivalentTo(["日本", "中国"], CollectionOrdering.Matching);
        await Assert.That(model.UseCases.Select(_ => _.Label)).IsEquivalentTo(["登录", "注册"], CollectionOrdering.Matching);
        await Assert.That(model.Relationships.Count).IsEqualTo(2);
    }

    // Each layer wins over the one before: the default class, the named classes in order, then `style`.
    [Test]
    public async Task Parse_StylePrecedence()
    {
        var model = Parse(
            """
            usecase-beta
            Checkout("Checkout"):::first
            class Checkout second
            classDef default stroke:#111,fill:#eee
            classDef first stroke:#222,stroke-width:3px
            classDef second stroke:#333
            style Checkout fill:#fde
            """);

        var style = model.UseCases.Single().Style!;
        await Assert.That(style.Stroke).IsEqualTo("#333");
        await Assert.That(style.StrokeWidth).IsEqualTo(3);
        await Assert.That(style.Fill).IsEqualTo("#fde");
    }

    [Test]
    public async Task Parse_RejectsSecondStatementOnALine()
    {
        var result = new UseCaseParser().Parse(
            """
            usecase-beta
            A --> B C --> D
            """);

        await Assert.That(result.Success).IsFalse();
    }

    [Test]
    public async Task DetectsType()
    {
        await Assert.That(Mermaid.TryDetectType("usecase-beta\nactor A", out var type)).IsTrue();
        await Assert.That(type).IsEqualTo(DiagramType.UseCase);
        await Assert.That(Mermaid.TryDetectType("---\ntitle: T\n---\nusecase-beta\nactor A", out var withFrontMatter)).IsTrue();
        await Assert.That(withFrontMatter).IsEqualTo(DiagramType.UseCase);
    }

    static UseCaseModel Parse(string input)
    {
        var result = new UseCaseParser().Parse(input);
        if (!result.Success)
        {
            throw new(result.Error);
        }

        return result.Value;
    }
}
