
using System.ClientModel;

using AutoGen.Core;
using AutoGen.DotnetInteractive;
using AutoGen.OpenAI.Extension;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using AutoGen.SemanticKernel.Extension;
using OpenAI;

using AutoGen.SemanticKernel;
using AutoGen.OpenAI;
using Microsoft.DotNet.Interactive;

using MultiAgent.Plugins;
using System.Management.Automation;


const string ModelLlama2 = "llama3.2";
const string ModelVision = "llava-phi3";
const string EndpointOllama = "http://localhost:11434";

string FILE_PATH = string.Empty;

var openaiClientOllama = CreateOpenAIClient(EndpointOllama);
var chatClientOthers = openaiClientOllama.GetChatClient(ModelLlama2);
var chatClientVision = openaiClientOllama.GetChatClient(ModelVision);

var kernelDotNet = InitializeDotnetKernel();
var semanticKernel = InitializeSemanticKernel(openaiClientOllama);

var settings = new OpenAIPromptExecutionSettings
{
  ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
};

OpenAIClient CreateOpenAIClient(string endpoint)
{
  return new OpenAIClient(new ApiKeyCredential("api-key"), new OpenAIClientOptions
  {
    Endpoint = new Uri(endpoint),
  });
}

CompositeKernel InitializeDotnetKernel()
{
  return DotnetInteractiveKernelBuilder
      .CreateDefaultInProcessKernelBuilder()
      .Build();
}

Microsoft.SemanticKernel.Kernel InitializeSemanticKernel(OpenAIClient openaiClient)
{
  return Microsoft.SemanticKernel.Kernel.CreateBuilder()
      .AddOpenAIChatCompletion(ModelLlama2, openaiClient, "not necessary")
      .Build();
}


//Plugins
var kernelPluginMiddlewarePdf = new KernelPluginMiddleware(semanticKernel, KernelPluginFactory.CreateFromType<PdfOperatorPlugin>());
var kernelPluginMiddlewareFilesystem = new KernelPluginMiddleware(semanticKernel, KernelPluginFactory.CreateFromType<FilePlugin>());
var userProxyAgent = await AgentFactory.CreateUserProxyAgent();

var visionAgent = new OpenAIChatAgent(chatClientVision, "VisionAgent",
  "You are the vision agent. You can analyze images and extract text from them as if you are an OCR system.", 0, -1)
  .RegisterMessageConnector()
  .RegisterPrintMessage();

// define the agents
var pdfManagerAgent = AgentFactory.CreatePdfExtractAgent( chatClientOthers, kernelPluginMiddlewarePdf);
var summarizerAgent = AgentFactory.CreateSummarizerAgent(chatClientOthers);
var titleExtractorAgent = AgentFactory.CreateTitleAgent(chatClientOthers);
var titleReviewerAgent = AgentFactory.CreateTitleReviewerAgent(chatClientOthers);
var renameManagerAgent = AgentFactory.CreateRenamingAgent(chatClientOthers, kernelPluginMiddlewareFilesystem);
var adminAgent = await AgentFactory.CreateAdminAsync(chatClientOthers);
var groupAdmin = await AgentFactory.CreateGroupAdminAsync(chatClientOthers);

var myOrchestrator = new MyRolePlayOrchestrator(
  groupAdmin,
  AgentFactory.CreateTransitions(
    adminAgent, 
    userProxyAgent, 
    pdfManagerAgent, 
    summarizerAgent, 
    titleExtractorAgent, 
    titleReviewerAgent, 
    renameManagerAgent)
    );

var groupChat = new GroupChat(
  orchestrator: myOrchestrator,
  members: [
    adminAgent, 
    pdfManagerAgent, 
    summarizerAgent, 
    titleExtractorAgent, 
    titleReviewerAgent, 
    userProxyAgent, 
    renameManagerAgent
    ]
  );

adminAgent.SendIntroduction("I will manage the group chat", groupChat);
pdfManagerAgent.SendIntroduction("I will manage the file and folder operations", groupChat);
summarizerAgent.SendIntroduction("I will summarize text from pdf file", groupChat);
titleExtractorAgent.SendIntroduction("I will extract title from summary", groupChat);
titleReviewerAgent.SendIntroduction("I will review title from title extractor", groupChat);
userProxyAgent.SendIntroduction("I will be the user", groupChat);

// Watch for file changes in the specified directory
void WatchDirectory(string path)
{
  FileSystemWatcher watcher = new FileSystemWatcher
  {
    Path = path,
    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
    Filter = "*.pdf"
  };

  watcher.Changed += async (sender, e) => await OnChanged(sender, e);
  watcher.EnableRaisingEvents = true;
}

// Event handler for file changes
async Task OnChanged(object source, FileSystemEventArgs e)
{
  Console.WriteLine($"File changed: {e.FullPath}");
  await StartAgentSystem(e.FullPath);
}

// Start the agent system with the filename
async Task StartAgentSystem(string filename)
{
  FILE_PATH = filename;
  var task = $"Find a title for the document '{filename}' and rename accordingly";
  var chatHistory = new List<IMessage>
  {
    new TextMessage(Role.User, task)
    {
      From = adminAgent.Name
    }
  };

  await foreach (var message in groupChat.SendAsync(chatHistory, maxRound: 20))
  {
    if (message != null && message.From == adminAgent.Name && message.GetContent().Contains("```summary"))
    {
      // Task complete!
      break;
    }
  }
}

//waiting for added files in the directory
var dir = "/Users/toto/_reise";
WatchDirectory(dir);

//uncomment for direct start
//await StartAgentSystem("/Users/toto/Projects/AIAgentsTest/code/CS/multiagent/Gescanntes Dokument.pdf");

//loop until ctrl c is pressed
Console.WriteLine($"Waiting for file changes in '{dir}'");
Console.WriteLine("\t(Press Ctrl+C to exit.)");
while (true)
{
  await Task.Delay(1000);
}

