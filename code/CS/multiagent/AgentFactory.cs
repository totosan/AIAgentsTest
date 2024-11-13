using System.Text;
using System.Text.Json;
using AutoGen.Core;
using AutoGen.DotnetInteractive.Extension;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using Microsoft.DotNet.Interactive;
using OpenAI.Chat;

public static class AgentFactory
{
#region CreateAgents
    // Creates a GroupAdmin agent with predefined settings
    public static async Task<IAgent> CreateGroupAdminAsync(ChatClient client)
    {
        var admin = new OpenAIChatAgent(
            chatClient: client,
            name: "GroupAdmin",
            systemMessage: "You are the admin of the group chat",
            temperature: 0,
            maxTokens: -1)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return admin;
    }

    // Creates an Admin agent with predefined settings
    public static async Task<IAgent> CreateAdminAsync(ChatClient client)
    {
        var admin = new OpenAIChatAgent(
            chatClient: client,
            name: "admin",
            temperature: 0,
            systemMessage: """
            You are a manager who takes coding problem from user and resolve problem by splitting them into small tasks and assign each task to the most appropriate agent.
            Here's available agents who you can assign task to:
            - coder: write csharp code to resolve task
            - reviewer: review csharp code from coder
            - runner: run csharp code from coder

            The workflow is as follows:
            - You take the coding problem from user
            - You break the problem into small tasks. For each tasks you first ask coder to write code to resolve the task. Once the code is written, you ask runner to run the code.
            - Once a small task is resolved, you summarize the completed steps and create the next step.
            - You repeat the above steps until the coding problem is resolved.

            You can use the following json format to assign task to agents:
            ```task
            {
                "to": "{agent_name}",
                "task": "{a short description of the task}",
                "context": "{previous context from scratchpad}"
            }
            ```

            If you need to ask user for extra information, you can use the following format:
            ```ask
            {
                "question": "{question}"
            }
            ```

            Once the coding problem is resolved, summarize each steps and results and send the summary to the user using the following format:
            ```summary
            @user, <summary of the task>
            ```

            Your reply must contain one of [```task|```ask|```summary] to indicate the type of your message.
            """,
            maxTokens: -1)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return admin;
    }

    // Creates a Coder agent with predefined settings
    public static async Task<IAgent> CreateCoderAgentAsync(ChatClient client)
    {
        var coder = new OpenAIChatAgent(
            chatClient: client,
            name: "coder",
            systemMessage: @"You act as dotnet coder, you write dotnet code to resolve task. Once you finish writing code, ask runner to run the code for you.
        DO NOT explain the code! JUST write the code.

        Here're some rules to follow on writing dotnet code:
        - put code between ```csharp and ```
        - Avoid adding `using` keyword when creating disposable object. e.g `var httpClient = new HttpClient()`
        - Try to use `var` instead of explicit type.
        - Try avoid using external library, use .NET Core library instead.
        - Use top level statement to write code.
        - Always print out the result to console. Don't write code that doesn't print out anything.
        - Don't repeat your self.
        If you need to install nuget packages, put nuget packages in the following format:
        
        ```nuget
        nuget_package_name
        ```
        Explanation of code is not allowed.
        If your code is incorrect, runner will tell you the error message. Fix the error and send the code again.",
            temperature: 0.4f)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return coder;
    }

    // Creates a CodeReviewer agent with predefined settings
    public static async Task<IAgent> CreateCodeReviewerAgentAsync(ChatClient client)
    {
        // code reviewer agent will review if code block from coder's reply satisfy the following conditions:
        // - There's only one code block
        // - The code block is csharp code block
        // - The code block is top level statement
        // - The code block is not using declaration
        var codeReviewAgent = new OpenAIChatAgent(
            chatClient: client,
            name: "reviewer",
            systemMessage: """
            You are a code reviewer who reviews code from coder. You need to check if the code satisfy the following conditions:
            - The reply from coder contains at least one code block, e.g ```csharp and ```
            - There's only one code block and it's csharp/c# code block

            You don't check the code style, only check if the code satisfy the above conditions.

            Put your comment between 
            ```review and ```, 
            if the code satisfies all conditions, put APPROVED in review.result field. Otherwise, put REJECTED along with comments. make sure your comment is clear and easy to understand.
            ## Example 1 ##
            ```review
            comment: The code satisfies all conditions.
            result: APPROVED
            ```

            ## Example 2 ##
            ```review
            comment: The code is inside main function. Please rewrite the code in top level statement.
            result: REJECTED
            ```

            """)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return codeReviewAgent;
    }

    // Creates a Runner agent with predefined settings
    public static async Task<IAgent> CreateRunnerAgentAsync(Kernel kernel)
    {
        var runner = new DefaultReplyAgent(
            name: "runner",
            defaultReply: "No code available.")
            .RegisterMiddleware(async (msgs, option, agent, _) =>
            {
                if (msgs.Any() && msgs.All(msg => msg.From != "coder"))
                {
                    return new TextMessage(Role.Assistant, "No code available. Coder please write code");
                }
                else
                {
                    var coderMsg = msgs.Last(msg => msg.From == "coder");
                    if (coderMsg.ExtractCodeBlock("```csharp", "```") is string code)
                    {
                        var codeResult = await kernel.RunSubmitCodeCommandAsync(code, "csharp");

                        codeResult = $"""
                    [RUNNER_RESULT]
                    {codeResult}
                    """;

                        return new TextMessage(Role.Assistant, codeResult)
                        {
                            From = "runner",
                        };
                    }
                    else
                    {
                        return new TextMessage(Role.Assistant, "No code available. Coder please write code");
                    }
                }
            })
            .RegisterPrintMessage();

        return runner;
    }
#endregion
#region CreateTransitions
    // Creates transitions between agents to define the workflow
    public static Graph CreateTransitions(IAgent admin, IAgent coderAgent, IAgent codeReviewAgent, IAgent runner, IAgent userProxy){
        var adminToCoderTransition = Transition.Create(admin, coderAgent, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != admin.Name)
            {
                return false;
            }

            return true;
        });
        var coderToReviewerTransition = Transition.Create(coderAgent, codeReviewAgent);
        var adminToRunnerTransition = Transition.Create(admin, runner, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != admin.Name)
            {
                return false;
            }

            // the previous messages should contain a message from coder
            var coderMessage = messages.FirstOrDefault(x => x.From == coderAgent.Name);
            if (coderMessage is null)
            {
                return false;
            }

            return true;
        });

        var runnerToAdminTransition = Transition.Create(runner, admin);

        var reviewerToAdminTransition = Transition.Create(codeReviewAgent, admin, async (from, to,messages) => {
            // the last message should be from reviewer
            var lastMessage = messages.Last();
            if (lastMessage.From != codeReviewAgent.Name)
            {
                return false;
            }

            return true;
        });

        var adminToUserTransition = Transition.Create(admin, userProxy, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != admin.Name)
            {
                return false;
            }

            return true;
        });

        var userToAdminTransition = Transition.Create(userProxy, admin);

        var workflow = new Graph(
            [
                adminToCoderTransition,
                coderToReviewerTransition,
                reviewerToAdminTransition,
                adminToRunnerTransition,
                runnerToAdminTransition, 
                adminToUserTransition,
                userToAdminTransition,
            ]);

        return workflow;
    }
#endregion
}