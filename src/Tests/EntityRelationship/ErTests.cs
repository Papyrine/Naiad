public class ErTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            erDiagram
                CUSTOMER ||--o{ ORDER : places
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task MultipleRelationships()
    {
        const string input =
            """
            erDiagram
                CUSTOMER ||--o{ ORDER : places
                ORDER ||--|{ LINE-ITEM : contains
                PRODUCT ||--o{ LINE-ITEM : includes
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Attributes()
    {
        const string input =
            """
            erDiagram
                CUSTOMER {
                    string name
                    string email
                    int age
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task KeyTypes()
    {
        const string input =
            """
            erDiagram
                CUSTOMER {
                    int id PK
                    string name
                    string email UK
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Comments()
    {
        const string input =
            """
            erDiagram
                CUSTOMER {
                    int id PK "Primary key"
                    string name "Customer name"
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task LowercaseKeyTypes()
    {
        const string input =
            """
            erDiagram
                CUSTOMER {
                    int id pk
                    int region_id fk
                    string email uk
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SizedTypes()
    {
        const string input =
            """
            erDiagram
                PRODUCT {
                    int id PK
                    nvarchar(200) name
                    decimal(18,2) price
                    varchar(max)(nullable) notes
                    string? sku "Optional"
                }
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Alias()
    {
        const string input =
            """
            erDiagram
                CUSTOMER["Customer Account"] {
                    int id PK
                    string name
                }
                ORDER[Purchase] {
                    int id PK
                    int customer_id FK
                }
                CUSTOMER ||--o{ ORDER : places
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task AliasBold()
    {
        const string input =
            """
            erDiagram
                Company["**Company**"] {
                    int Id pk
                    nvarchar(200) Name
                }
                Employee["**Employee**: People who work here"] {
                    int Id pk
                    int CompanyId
                }
                Company ||--o{ Employee : "FK_Employee_Company"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SqlSchema()
    {
        const string input =
            """
            erDiagram
              Company["**Company**"] {
                int Id pk
                nvarchar(200) Name
              }
              Employee["**Employee**: People who work here"] {
                int Id pk "computed: the key"
                nvarchar(100) FirstName
                nvarchar(100)(nullable) LastName
                decimal(18,2) Salary
                int CompanyId
                int(nullable) ManagerId "reports to"
              }
              Order_Detail["**Order_Detail**"] {
                int Id pk
                int EmployeeId
                nvarchar(max)(nullable) Notes
              }
              Company ||--o{ Employee : "FK_Employee_Company"
              Employee ||--o{ Employee : "FK_Employee_Manager"
              Employee ||--o{ Order_Detail : "FK_OrderDetail_Employee"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task OneToOne()
    {
        const string input =
            """
            erDiagram
                PERSON ||--|| PASSPORT : has
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ZeroOrOne()
    {
        const string input =
            """
            erDiagram
                EMPLOYEE |o--o| PARKING-SPACE : uses
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task NonIdentifying()
    {
        const string input =
            """
            erDiagram
                CUSTOMER ||..o{ ORDER : places
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Compelx()
    {
        const string input =
            """
            erDiagram
            CUSTOMER {
                int customer_id PK "Primary key"
                string first_name "Customer first name"
                string last_name "Customer last name"
                string email UK "Unique email address"
                date date_of_birth
                string phone
                boolean is_active
            }
            
            ADDRESS {
                int address_id PK
                int customer_id FK
                string street
                string city
                string state
                string postal_code
                string country
                string address_type "billing or shipping"
            }
            
            ORDER {
                int order_id PK
                int customer_id FK
                int shipping_address_id FK
                int billing_address_id FK
                datetime order_date
                datetime shipped_date
                string status
                decimal total_amount
            }
            
            ORDER_ITEM {
                int item_id PK
                int order_id FK
                int product_id FK
                int quantity
                decimal unit_price
                decimal discount
            }
            
            PRODUCT {
                int product_id PK
                int category_id FK
                string name
                string description
                decimal price
                int stock_quantity
                string sku UK
            }
            
            CATEGORY {
                int category_id PK
                int parent_id FK "Self-referencing"
                string name
                string description
            }
            
            CUSTOMER ||--o{ ORDER : places
            CUSTOMER ||--o{ ADDRESS : has
            ORDER ||--|{ ORDER_ITEM : contains
            ORDER }o--|| ADDRESS : "ships to"
            ORDER }o--|| ADDRESS : "bills to"
            PRODUCT ||--o{ ORDER_ITEM : "included in"
            CATEGORY ||--o{ PRODUCT : categorizes
            CATEGORY |o--o| CATEGORY : "parent of"
            """;

        return VerifySvg(input);
    }

    // Entities named on a line of their own or in quotes, an attribute with two keys, cardinalities and
    // line styles written out in words, a comment inside a body, and a left-to-right direction.
    [Test]
    public Task WordsKeysAndStandaloneEntities()
    {
        const string input =
            """
            erDiagram
                direction LR
                CAR 1 to zero or more NAMED-DRIVER : allows
                PERSON many(0) optionally to 0+ NAMED-DRIVER : is
                "Insurance Policy" only one to one or more CAR : covers
                AUDIT-LOG
                NAMED-DRIVER {
                    %% the two keys make the row's key column wider
                    string carRegistration PK, FK
                    string driverLicence PK, FK "also the person's id"
                    date since
                }
                "Insurance Policy" {
                    string number PK
                }
            """;

        return VerifySvg(input);
    }
}
