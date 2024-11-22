using System.ClientModel;
using System.Speech.Recognition;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AutoGen;
using AutoGen.Core;
using AutoGen.DotnetInteractive;
using AutoGen.DotnetInteractive.Extension;
using AutoGen.OpenAI;
using AutoGen.OpenAI.Extension;
using AutoGen.SemanticKernel;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    public static ChatClient ChatClientOthers;
    static ChatClient ChatClientVision;
    public static KernelPluginMiddleware KernelPluginMiddlewarePdf;
    public static KernelPluginMiddleware KernelPluginMiddlewareFilesystem;

    #region inits
    static OpenAIPromptExecutionSettings settings = new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
    };

    public static async Task InitFactory(string endpointOllama, string ModelLlama2, string ModelVision)
    {
        var openaiClientOllama = CreateOpenAIClient(endpointOllama);
        ChatClientOthers = openaiClientOllama.GetChatClient(ModelLlama2);
        ChatClientVision = openaiClientOllama.GetChatClient(ModelVision);
        EndpointOllama = endpointOllama;

        var dotNetKernel = InitializeDotnetKernel();
        var semanticKernel = InitializeSemanticKernel(openaiClientOllama, ModelLlama2);

        //Plugins
        KernelPluginMiddlewarePdf = new KernelPluginMiddleware(semanticKernel, KernelPluginFactory.CreateFromType<PdfOperatorPlugin>());
        KernelPluginMiddlewareFilesystem = new KernelPluginMiddleware(semanticKernel, KernelPluginFactory.CreateFromType<FilePlugin>());
    }
    static OpenAIClient CreateOpenAIClient(string endpoint)
    {
        return new OpenAIClient(new ApiKeyCredential("api-key"), new OpenAIClientOptions
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

    public static async Task<IAgent> CreateVisionAgent()
    {
        return new OpenAIChatAgent(ChatClientVision, "VisionAgent",
      "You are the vision agent. You can analyze images and extract text from them as if you are an OCR system.", 0, -1)
      .RegisterMessageConnector()
      .RegisterPrintMessage();
    }

    public static async Task<IAgent> CreateUserProxyAgent()
    {
        var userProxy = new DefaultReplyAgent(name: "user", defaultReply: GroupChatExtension.TERMINATE)
            .RegisterPrintMessage();
        return userProxy;
    }
    // Creates a GroupAdmin agent with predefined settings
    public static async Task<IAgent> CreateGroupAdminAsync()
    {
        var admin = new OpenAIChatAgent(
            chatClient: ChatClientOthers,
            name: "GroupAdmin",
            systemMessage: "You are the admin of the group chat",
            temperature: 0,
            maxTokens: -1)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return admin;
    }

    // Creates an Admin agent with predefined settings
    public static async Task<IAgent> CreateAdminAsync()
    {

        var admin = new OpenAIChatAgent(
            chatClient: ChatClientOthers,
            name: "admin",
            temperature: 0,
            systemMessage: """
            You are a manager who takes file operations from user and resolve problem by splitting them into small tasks and assign each task to the most appropriate agent.
            
            The workflow is as follows:
            -   admin --> pdfManagerAgent 
                pdfManagerAgent --> summarizerAgent 
                summarizerAgent --> titleExtractorAgent
                titleExtractor --> titleReviewerAgent
                titleReviewerAgent --> admin
                titleReviewerAgent --> titleExtractor
                titelReviewer --> fileManagerAgent
                fileManagerAgent --> titleReviewerAgent
                --> admin
                admin --> userProxyAgent

            - You take the request from the user.
            - You can break down the problem into smaller tasks and assign them to the most appropriate agent. 
            - if a title was found, the reviewer will review the title and give feedback. According to the feedback, you can decide to change the title or keep it.
            - Do until the reviewer is satisfied with the title or the rounds are over.

            You can use the following json format to assign task to agents:
            ```task
            {
                "to": "{agent_name}",
                "task": "{a short description of the task}",
                "context": "{previous context from scratchpad}"
            }
            ```
            Once the task is completed, the agent will send a message with the following format:
            ```task
            {
                "task": "{task_description}",
                "status": "{COMPLETED/FAILED}",
                "result": "{result}"
            }
            ```

            Your reply must contain one of [```task] to indicate the type of your message.
            """,
            maxTokens: -1)
            .RegisterMessageConnector()
            .RegisterPrintMessage();

        return admin;
    }

    public static MiddlewareAgent<MiddlewareStreamingAgent<OpenAIChatAgent>> CreatePdfExtractAgent()
    {

        return new OpenAIChatAgent(ChatClientOthers, "PdfManager",
        "You are the pdf manager. You have tools to work with real pdf, which includes opening PDF to get its content.", 0, -1)
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

    public static MiddlewareStreamingAgent<OpenAIChatAgent> CreateSummarizerAgent()
    {
        return new OpenAIChatAgent(ChatClientOthers, "Summarizer", """
    You are the text summarizer. That's what you just do. For a given text, you create a short summary from.
    To work optimized for the overall goal (generate a title), you should create a summary that is short but still contains the most important information.
    Most title reflect a document of an expenses docuement (hotel invoice, gasoline bill, parking ticket, etc.)

    Your key information to focus on are:
    - Hotel name
    - Location
    - booking details
    - Travel dates
    - vehicle details
    - gasoline details
    - flight details
    - parking (name, duration, ...)
    - ...

    DON'T CARE ABOUT prices, amounts, etc. Just the key information that is important for the title.
    everything, that could be taken as a title.
    """,
            temperature: 0)
          .RegisterMessageConnector()
          .RegisterPrintMessage();
    }

    public static MiddlewareStreamingAgent<OpenAIChatAgent> CreateTitleAgent()
    {
        return new OpenAIChatAgent(ChatClientOthers, "TitleExtractor",
          @"You are the title extractor. Your task is to take the most matching elements of a summary for your task and make a short but marking title for a file.
  Rules:
  - You have to respect the length, speaking sense and filesystem constrains on macOs.
  - Country should be with country code (e.g. DE, US, CH, NL, ...)
  - Date should be in the format YYYY_MM_DD
  - start always with the type of document (hotel, bill, invoice, parking, ....)
  - DON'T use special chars as first char (e.g. #, @, ...)

  you generate a title in a format like the examples below:
  
  # FORMAT AND EXAMPLE 1 for Hotel
    <HOTELNAME>_<LOCATION>_<DATE>
    e.g. Hilton_London_2022-12-31

  # FORMAT AND EXAMPLE 2 for Gasoline
    TANKEN_<LOCATION>_<GASOLINENAME>_<GASTYPE>_<YEAR_MONTH>
    e.g. TANKEN_GERMANY_RASTHOF_DIESEL_23_05
  
  # FORMAT AND EXAMPLE 3 for Parking
    PARKING_<PARKINGLOT-IDENTIFIER>_<LOCATION>_<DATE>
    e.g. PARKING_URANIA_ZURICH_2022-12-31
    
    ",
            temperature: 0)
          .RegisterMessageConnector()
          .RegisterPrintMessage();
    }

    public static MiddlewareStreamingAgent<OpenAIChatAgent> CreateTitleReviewerAgent()
    {
        return new OpenAIChatAgent(ChatClientOthers, "TitleReviewer",
          @"You are the title reviewer. You can make comments to a title respecting length, speaking sense and filesystem constrains on macOs.
  Rules:
  - You have to respect the length, speaking sense and filesystem constrains on macOs.
  - Country should be with country code (e.g. DE, US, CH, NL, ...)
  - Date should be in the format YYYY_MM_DD
  - start always with the type of document (hotel, bill, invoice, parking, ....)
  - DON'T use special chars as first char (e.g. #, @, ...)
 General:
  - Your task is to check, that the title is correct and alignes with the documents content.
  - You can also suggest improvements to the title.
  - You can also reject the title and ask for a new one. In that case ask for a new summary, for a complete new title.
  
  Some simplifications:
  - a flight need not to have the airline name in the title, 'FLIGHT' as identifier is enough.
   
  Put your comment between ```review and ```, if the title satisfies all conditions, put APPROVED in review.result field.
  Otherwise, put REJECTED along with comments. make sure your comment is clear and easy to understand.
    
    See these examples:

  #EXAMPLE 1
  If you are satisfied with the title, you can approve it by sending a message with the following format:
  ```review
  comment: the comment, you want to send
  status: APPROVED
  
  # EXAMPLE 2
  if you are not satisfied with the title, you can reject it by sending a message with the following format:
  ```review
  comment: the comment, you want to send
  status: REJECTED
  ```
  ",
            temperature: 0)
          .RegisterMessageConnector()
          .RegisterPrintMessage();
    }

    public static MiddlewareAgent<MiddlewareStreamingAgent<OpenAIChatAgent>> CreateRenamingAgent()
    {
        return new OpenAIChatAgent(ChatClientOthers, "FilesystemManager",
          @"You are a filesystem manager. You can manage files and folders on the filesystem. You can list files in a folder, create a folder, delete a file, etc.
  Your main task is to rename files. Therefore, you have to find the original filename in the chat history and rename the file accordingly with the new filename.",
          temperature: 0)
          .RegisterMessageConnector()
          .RegisterMiddleware(KernelPluginMiddlewareFilesystem)
          .RegisterPrintMessage();
    }

    #endregion
    #region CreateTransitions

    /// <summary>
    /// Creates a workflow graph with transitions between various agents.
    /// </summary>
    /// <param name="admin">The admin agent.</param>
    /// <param name="userProxyAgent">The user proxy agent.</param>
    /// <param name="pdfManagerAgent">The PDF manager agent.</param>
    /// <param name="summarizerAgent">The summarizer agent.</param>
    /// <param name="titleExtractorAgent">The title extractor agent.</param>
    /// <param name="titleReviewerAgent">The title reviewer agent.</param>
    /// <param name="fileManagerAgent">The file manager agent.</param>
    /// <returns>A Graph object representing the workflow with all defined transitions.</returns>
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