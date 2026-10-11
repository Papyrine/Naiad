public class RequirementTests : TestBase
{
    [Test]
    public Task Simple()
    {
        var input =
            """
            requirementDiagram

            requirement test_req {
                id: 1
                text: The system shall do something
                risk: high
                verifymethod: test
            }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Functional()
    {
        var input =
            """
            requirementDiagram

            functionalRequirement login_req {
                id: REQ-001
                text: User must be able to log in
                risk: medium
                verifymethod: demonstration
            }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Element()
    {
        var input =
            """
            requirementDiagram

            requirement test_req {
                id: 1
                text: System requirement
                risk: low
            }

            element test_entity {
                type: simulation
            }

            test_entity - satisfies -> test_req
            """;

        return VerifySvg(input);
    }

    // Nothing declared but the name, so the box carries no rows and no separator — and in particular no
    // invented risk or verification method.
    [Test]
    public Task NoProperties()
    {
        var input =
            """
            requirementDiagram

            requirement bare_req {
            }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Multiple()
    {
        var input =
            """
            requirementDiagram

            requirement req1 {
                id: REQ-001
                text: First requirement
                risk: low
            }

            requirement req2 {
                id: REQ-002
                text: Second requirement
                risk: medium
            }

            performanceRequirement perf1 {
                id: PERF-001
                text: Performance must be good
                risk: high
            }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        var input =
            """
            requirementDiagram

            requirement user_auth {
                id: REQ-001
                text: Users must authenticate
                risk: high
                verifymethod: test
            }

            functionalRequirement login_page {
                id: REQ-002
                text: System provides login page
                risk: medium
            }

            element web_app {
                type: application
                docref: /docs/webapp
            }

            element login_module {
                type: module
            }

            web_app - contains -> login_module
            login_module - satisfies -> login_page
            login_page - derives -> user_auth
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task AllTypes()
    {
        var input =
            """
            requirementDiagram

            requirement req1 {
                id: 1
                text: Main requirement
            }

            requirement req2 {
                id: 2
                text: Derived requirement
            }

            element elem1 {
                type: component
            }

            req2 - derives -> req1
            elem1 - satisfies -> req1
            elem1 - verifies -> req2
            """;

        return VerifySvg(input);
    }

    // The layout Mermaid's own examples use: the closing brace indented with the properties, a quoted
    // name and value, a comment and a blank line inside a block, and a relation written right to left.
    [Test]
    public Task IndentedBlocksAndReverseRelation()
    {
        const string input =
            """
            requirementDiagram

                requirement test_req {
                id: 1
                text: the test text.
                risk: high
                verifymethod: test
                }

                functionalRequirement "Second requirement" {
                id: 1.1
                %% quoted, so the quotes are not part of the text
                text: "the second test text."

                risk: low
                verifymethod: inspection
                }

                element test_entity {
                type: "test suite"
                docRef: reqs/test_entity
                }

                test_entity - satisfies -> test_req
                test_req - traces -> "Second requirement"
                "Second requirement" <- verifies - test_entity
            """;

        return VerifySvg(input);
    }
}