using System.ClientModel;
using System.Text.Json;
using AutoGen.Core;
using AutoGen.DotnetInteractive;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using Microsoft.DotNet.Interactive;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using MultiAgent.Plugins;
using OpenAI;
using OpenAI.Chat;
using static MultiAgent.Plugins.PdfExtractorizer;

public static class AgentFactory
{

    public static string EndpointOllama;
    public static ChatClient ChatClientToolsCallLlama32;
    public static ChatClient ChatClientTextPhi35;
    static ChatClient ChatClientVision;
    public static KernelPluginMiddleware KernelPluginMiddlewarePdf;
    public static KernelPluginMiddleware KernelPluginMiddlewareFilesystem;

    #region inits
    static OpenAIPromptExecutionSettings settings = new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
    };

    /// <summary>
    /// Initializes the factory with the specified endpoints and models.
    /// </summary>
    /// <param name="endpointOllama">The endpoint for the Ollama service.</param>
    /// <param name="ModelLlama2">The model identifier for the Llama2 chat client.</param>
    /// <param name="ModelVision">The model identifier for the Vision chat client.</param>
    /// <returns>A task that represents the asynchronous initialization operation.</returns>
    public static async Task InitFactory(string endpointOllama, string ModelLlama2, string ModelPhi35, string ModelVision)
    {
        EndpointOllama = endpointOllama;

        var openaiClientOllama = CreateOpenAIClient(endpointOllama);

        ChatClientTextPhi35 = openaiClientOllama.GetChatClient(ModelPhi35);
        ChatClientToolsCallLlama32 = openaiClientOllama.GetChatClient(ModelLlama2);
        ChatClientVision = openaiClientOllama.GetChatClient(ModelVision);

        // Kernels for different agents capabilities
        var dotNetKernel = InitializeDotnetKernel(); // running created C# code
        var semanticKernel = InitializeSemanticKernel(openaiClientOllama, ModelLlama2); //calling OpenAI API compatible tools

        //Plugins
        KernelPluginMiddlewarePdf = new KernelPluginMiddleware(semanticKernel, KernelPluginFactory.CreateFromType<PdfOperatorPlugin>());
        KernelPluginMiddlewareFilesystem = new KernelPluginMiddleware(semanticKernel, KernelPluginFactory.CreateFromType<FilePlugin>());
    }

    static OpenAIClient CreateOpenAIClient(string endpoint)
    {
        return new OpenAIClient(new ApiKeyCredential("not-needed"), new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
        });
    }

    static CompositeKernel InitializeDotnetKernel()
    {
        return DotnetInteractiveKernelBuilder
            .CreateDefaultInProcessKernelBuilder()
            .Build();
    }

    static Microsoft.SemanticKernel.Kernel InitializeSemanticKernel(OpenAIClient openaiClient, string modelLlama2)
    {
        return Microsoft.SemanticKernel.Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(modelLlama2, openaiClient, "not necessary")
            .Build();
    }
    #endregion


    #region CreateAgents

    /// <summary>
    /// Creates a Vision agent with predefined settings.
    /// </summary>
    public static async Task<IAgent> CreateVisionAgent()
    {
        return new OpenAIChatAgent(ChatClientVision, "VisionAgent", SystemMessages.SystemMessageVision, 0, -1)
      .RegisterMessageConnector()
      .RegisterPrintMessage();
    }

    /// <summary>
    /// Creates a UserProxy agent with predefined settings.
    /// </summary>
    public static async Task<IAgent> CreateUserProxyAgent()
    {
        var userProxy = new DefaultReplyAgent(name: "user", defaultReply: GroupChatExtension.TERMINATE)
            .RegisterPrintMessage();
        return userProxy;
    }

    /// <summary>
    /// Creates a GroupAdmin agent with predefined settings.
    /// </summary>
    /// <returns></returns>
    public static async Task<IAgent> CreateGroupAdminAsync()
    {
        var admin = new OpenAIChatAgent(
            chatClient: ChatClientToolsCallLlama32,
            name: "GroupAdmin",
            systemMessage: SystemMessages.SystemMessageGroupAdmin,
            temperature: 0,
            maxTokens: -1)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return admin;
    }

    /// <summary>
    /// Creates an Admin agent with predefined settings
    /// </summary>
    public static async Task<IAgent> CreateAdminAsync()
    {

        var admin = new OpenAIChatAgent(
            chatClient: ChatClientToolsCallLlama32,
            name: "admin",
            temperature: 0,
            systemMessage: SystemMessages.SystemMessageAdmin,
            maxTokens: -1)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return admin;
    }

    /// <summary>
    /// Creates a PdfExtract agent with predefined settings.
    /// </summary>
    public static MiddlewareAgent<MiddlewareStreamingAgent<OpenAIChatAgent>> CreatePdfExtractAgent()
    {

        return new OpenAIChatAgent(ChatClientToolsCallLlama32, "PdfManager",
        SystemMessages.SystemMessagePdfExtractor, 0, -1)
          .RegisterMessageConnector()
          .RegisterMiddleware(KernelPluginMiddlewarePdf)
          .RegisterMiddleware(async (msgs, option, agent, ct) =>
          {

              var reply = await agent.GenerateReplyAsync(msgs, option, ct);
              var tools = reply.GetToolCalls();
              //funcitnoARguments "{\"filePath\":\"/Users/toto/_reise/#PARKING_SCHWEIZERISCHE_24-11.pdf\"}"
              // get the file path from the function arguments
              var functionExtract = tools.Where(t => t.FunctionName == "ExtractPdfContent").FirstOrDefault();
              var result = functionExtract.FunctionArguments;
              var filePath = JsonSerializer.Deserialize<FunctionCallResultType>(result).filePath;
              if (reply.GetContent().Contains("IMAGE"))
              {
                  var images = PdfExtractorizer.ExtractImagesFromPdf(filePath);
                  var texts = PdfExtractorizer.ExtractTextFromImage(images[0]);
                  reply = new TextMessage(Role.User, texts, from: agent.Name);
              }
              return reply;
          })
          .RegisterPrintMessage();
    }
    /// <summary>
    /// Creates a Summarizer agent with predefined settings.
    /// </summary>
    public static MiddlewareStreamingAgent<OpenAIChatAgent> CreateSummarizerAgent()
    {
        return new OpenAIChatAgent(ChatClientToolsCallLlama32, "Summarizer", SystemMessages.SystemMessageSummarizer,
            temperature: 0)
          .RegisterMessageConnector()
          .RegisterPrintMessage();
    }

    /// <summary>
    /// Creates a Title agent with predefined settings.
    /// </summary>
    public static MiddlewareStreamingAgent<OpenAIChatAgent> CreateTitleAgent()
    {
        return new OpenAIChatAgent(ChatClientToolsCallLlama32, "TitleExtractor",
          SystemMessages.SystemMessageTitleCreator,
            temperature: 0)
          .RegisterMessageConnector()
          .RegisterPrintMessage();
    }
    /// <summary>
    /// Creates a TitleReviewer agent with predefined settings.
    /// </summary>
    public static MiddlewareStreamingAgent<OpenAIChatAgent> CreateTitleReviewerAgent()
    {
        return new OpenAIChatAgent(ChatClientToolsCallLlama32, "TitleReviewer",
          SystemMessages.SystemMessageTitleReviewer,
            temperature: 0)
          .RegisterMessageConnector()
          .RegisterPrintMessage();
    }
    /// <summary>
    /// Creates a Renaming agent with predefined settings.
    /// </summary>
    public static MiddlewareAgent<MiddlewareStreamingAgent<OpenAIChatAgent>> CreateRenamingAgent()
    {
        return new OpenAIChatAgent(ChatClientToolsCallLlama32, "FilesystemManager",
          SystemMessages.SystemMessageFilesystemManager,
          temperature: 0)
          .RegisterMessageConnector()
          .RegisterMiddleware(KernelPluginMiddlewareFilesystem)
          .RegisterPrintMessage();
    }

    #endregion


    #region CreateTransitions

    /// <summary>
    /// Creates the transitions for the agents.
    /// </summary>
    /// <param name="admin"></param>
    /// <param name="userProxyAgent"></param>
    /// <param name="pdfManagerAgent"></param>
    /// <param name="summarizerAgent"></param>
    /// <param name="titleExtractorAgent"></param>
    /// <param name="titleReviewerAgent"></param>
    /// <param name="fileManagerAgent"></param>
    /// <returns>Returns the transition graph</returns>
    public static Graph CreateTransitions(IAgent admin, IAgent userProxyAgent, IAgent pdfManagerAgent, IAgent summarizerAgent, IAgent titleExtractorAgent, IAgent titleReviewerAgent, IAgent fileManagerAgent)
    {

        // ADMIN --> PDFMANAGER
        var admin2PdfTransition = Transition.Create(admin, pdfManagerAgent, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From != admin.Name)
            {
                return false;
            }

            return true;
        });

        // REVIEWER --> FILEMANAGER
        var reviewer2FileManagerTransition = Transition.Create(titleReviewerAgent, fileManagerAgent, async (from, to, messages) =>
        {
            // the last message should be from titleReviewerAgent
            var lastMessage = messages.Last();
            if (lastMessage.From == titleReviewerAgent.Name && lastMessage.GetContent().Contains("APPROVED"))
            {
                return true;
            }

            return false;
        });

        // PDFMANAGER --> SUMMARIZER
        var pdf2SummarizerTransition = Transition.Create(pdfManagerAgent, summarizerAgent, async (from, to, messages) =>
        {
            // the last message should be from pdfExtractorAgent
            var lastMessage = messages.Last();
            if (lastMessage.From != pdfManagerAgent.Name)
            {
                return false;
            }

            return true;
        });

        // SUMMARIZER --> TITLEEXTRACTOR
        var summarizer2TitleExtractorTransition = Transition.Create(summarizerAgent, titleExtractorAgent, async (from, to, messages) =>
        {
            // the last message should be from summarizerAgent
            var lastMessage = messages.Last();
            if (lastMessage.From != summarizerAgent.Name)
            {
                return false;
            }

            return true;
        });

        // TITLEEXTRACTOR --> TITLEREVIEWER
        var titleExtractor2TitleReviewerTransition = Transition.Create(titleExtractorAgent, titleReviewerAgent, async (from, to, messages) =>
        {
            // the last message should be from titleExtractorAgent
            var lastMessage = messages.Last();
            if (lastMessage.From != titleExtractorAgent.Name)
            {
                return false;
            }

            return true;
        });

        // TITLEREVIEWER --> TITLEEXTRACTOR
        var titleReviewer2TitleExtractorTransition = Transition.Create(titleReviewerAgent, titleExtractorAgent, async (from, to, messages) =>
        {
            // the last message should be from titleReviewerAgent
            var lastMessage = messages.Last();
            if (lastMessage.From == titleReviewerAgent.Name && lastMessage.GetContent().Contains("REJECTED"))
            {
                return true;
            }

            return false;
        });

        // FILEMANAGER --> TITLEREVIEWER
        var fileManager2TitleReviewerTransition = Transition.Create(fileManagerAgent, titleReviewerAgent, async (from, to, messages) =>
        {
            // the last message should be from fileManagerAgent
            var lastMessage = messages.Last();
            if (lastMessage.From == fileManagerAgent.Name && lastMessage.GetContent().Contains("already exists"))
            {
                return true;
            }

            return false;
        });

        // ADMIN --> USERPROXY
        var admin2UserProxyTransition = Transition.Create(admin, userProxyAgent, async (from, to, messages) =>
        {
            // the last message should be from admin
            var lastMessage = messages.Last();
            if (lastMessage.From == admin.Name && lastMessage.GetContent().Contains("```summary"))
            {
                return true;
            }

            return false;
        });

        // Create the workflow graph with all transitions
        var workflow = new Graph(
          [
            admin2PdfTransition,
            pdf2SummarizerTransition,
            summarizer2TitleExtractorTransition,
            titleExtractor2TitleReviewerTransition,
            titleReviewer2TitleExtractorTransition,
            fileManager2TitleReviewerTransition,
            admin2UserProxyTransition,
           reviewer2FileManagerTransition
          ]);

        return workflow;
    }

    #endregion
}