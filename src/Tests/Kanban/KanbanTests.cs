public class KanbanTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            kanban
            todo[Todo]
                task1[First Task]
                task2[Second Task]
            done[Done]
                task3[Completed Task]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ThreeColumns()
    {
        const string input =
            """
            kanban
            todo[To Do]
                t1[Research]
                t2[Design]
            wip[In Progress]
                t3[Development]
            done[Done]
                t4[Testing]
                t5[Review]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task EmptyColumns()
    {
        const string input =
            """
            kanban
            backlog[Backlog]
            todo[To Do]
            done[Done]
                t1[Task 1]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task ManyTasks()
    {
        const string input =
            """
            kanban
            col1[Sprint Backlog]
                t1[User Story 1]
                t2[User Story 2]
                t3[User Story 3]
                t4[User Story 4]
                t5[User Story 5]
            col2[In Progress]
                t6[Feature A]
                t7[Feature B]
            col3[Review]
                t8[Bug Fix 1]
            col4[Done]
                t9[Setup]
                t10[Configuration]
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SingleColumn()
    {
        const string input =
            """
            kanban
            tasks[All Tasks]
                t1[Task One]
                t2[Task Two]
                t3[Task Three]
            """;

        return VerifySvg(input);
    }

    // Columns named by bare text, by a label alone and by id[label]; tasks with and without ids; a name
    // long enough to wrap; and the ticket, assignee and priority a task can carry.
    [Test]
    public Task ColumnsWithoutIdsAndTaskMetadata()
    {
        const string input =
            """
            kanban
              Todo
                [Create Documentation]
                docs[Create Blog about the new diagram]
              [In progress]
                id6[Create renderer so that it works in all cases. We also add some extra text here for testing purposes.]
              id10[Ready for test]
                id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
                id66[last item]@{ priority: 'Very Low', assigned: 'knsv' }
              id11[Done]
                id5[define getData]
                id2[Update DB function]@{ ticket: MC-2037, priority: 'Very High' }
            """;

        return VerifySvg(input);
    }
}
