using System.ClientModel;
using System.ComponentModel;
using AutoGen.Core;
using AutoGen.DotnetInteractive;
using AutoGen.OpenAI.Extension;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using AutoGen.SemanticKernel.Extension;
using OpenAI;
using System.DirectoryServices.Protocols;
using AutoGen.SemanticKernel;
using AutoGen.OpenAI;
using Microsoft.DotNet.Interactive;

const string ModelLlama = "bartowski/Llama-3.2-3B-Instruct-GGUF/Llama-3.2-3B-Instruct-Q8_0.gguf";
const string ModelCoder = "legraphista/Codestral-22B-v0.1-IMat-GGUF/Codestral-22B-v0.1-hf.IQ1_S.gguf";
const string ModelLlama2 = "llama3.2";
const string ModelCoder2 = "phi3.5";
const string EndpointOllama = "http://localhost:11434";
const string EndpointLmStudio = "http://localhost:1234";

var openaiClientLmStudio = CreateOpenAIClient(EndpointLmStudio);
var openaiClientOllama = CreateOpenAIClient(EndpointOllama);

var chatClientOthers = openaiClientOllama.GetChatClient(ModelLlama2);
//var chatClientOthers = openaiClientLmStudio.GetChatClient(ModelLlama);
//var chatClientCoder = openaiClientOllama.GetChatClient(ModelCoder2);
var chatClientCoder = openaiClientLmStudio.GetChatClient(ModelCoder);

var kernelDotNet = InitializeDotnetKernel();
var semanticKernel = InitializeSemanticKernel(openaiClientLmStudio);

var settings = new OpenAIPromptExecutionSettings
{
  ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
};

var plugin = InitializePlugins(semanticKernel);

KernelPlugin localTimePlugin = KernelPluginFactory.CreateFromType<LocalTimePlugin>();
var kernelPluginMiddlewareTime = new KernelPluginMiddleware(semanticKernel, localTimePlugin);

var userProxy = new DefaultReplyAgent(name: "user", defaultReply: GroupChatExtension.TERMINATE)
    .RegisterPrintMessage();

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
      .AddOpenAIChatCompletion(ModelLlama2, openaiClient, "Llama")
      .Build();
}

KernelPlugin InitializePlugins(Microsoft.SemanticKernel.Kernel semanticKernel)
{
  var getWeatherFunction = KernelFunctionFactory.CreateFromMethod(
      method: (string location) => $"The weather in {location} is 75 degrees Fahrenheit.",
      functionName: "GetWeather",
      description: "Get the weather for a location.");

  return semanticKernel.CreatePluginFromFunctions("my_plugin", new[] { getWeatherFunction });
}

var kernelPluginMiddlewareWeather = new KernelPluginMiddleware(semanticKernel, plugin);

var coder = await AgentFactory.CreateCoderAgentAsync(chatClientCoder);
var reviewer = await AgentFactory.CreateCodeReviewerAgentAsync(chatClientCoder);
var runner = await AgentFactory.CreateRunnerAgentAsync(kernelDotNet);
var admin = await AgentFactory.CreateAdminAsync(chatClientOthers);
var groupAdmin = await AgentFactory.CreateGroupAdminAsync(chatClientOthers);
var tool = new OpenAIChatAgent(chatClientOthers, "tool", temperature: 0, maxTokens: -1)
    .RegisterMessageConnector()
    .RegisterMiddleware(kernelPluginMiddlewareTime)
    .RegisterPrintMessage();

await tool.SendAsync(new TextMessage(Role.User, "What time is it?"));

var myOrchestrator = new MyRolePlayOrchestrator(groupAdmin, AgentFactory.CreateTransitions(admin, coder, reviewer, runner, userProxy));
var groupChat = new GroupChat(
  members: new IAgent[] { admin, coder, runner, reviewer, userProxy },
  orchestrator: new MyRolePlayOrchestrator(groupAdmin, AgentFactory.CreateTransitions(admin, coder, reviewer, runner, userProxy))
  );

admin.SendIntroduction("I will manage the group chat", groupChat);
coder.SendIntroduction("I will write dotnet code to resolve task", groupChat);
reviewer.SendIntroduction("I will review dotnet code", groupChat);
runner.SendIntroduction("I will run dotnet code", groupChat);

var task = "What's the 39th of fibonacci number?";
var chatHistory = new List<IMessage>
{
  new TextMessage(Role.Assistant, task)
  {
    From = userProxy.Name
  }
};
Console.WriteLine("Please enter the task you want to solve:");
task = Console.ReadLine();
chatHistory[0] = new TextMessage(Role.Assistant, task)
{
  From = userProxy.Name
};
await foreach (var message in groupChat.SendAsync(chatHistory, maxRound: 10))
{
  if (message != null && message.From == admin.Name && message.GetContent().Contains("```summary"))
  {
    // Task complete!
    break;
  }
}


