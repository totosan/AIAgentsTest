
using AutoGen.Core;

const string ModelLlama2 = "llama3.2";
const string ModelPhi35 = "phi3.5";
const string ModelVision = "llava-phi3";
const string EndpointOllama = "http://localhost:11434";

string FILE_PATH = string.Empty;

await AgentFactory.InitFactory(EndpointOllama, ModelLlama2, ModelPhi35, ModelVision);

// define the agents
var userProxyAgent = await AgentFactory.CreateUserProxyAgent();
var pdfManagerAgent = AgentFactory.CreatePdfExtractAgent();
var summarizerAgent = AgentFactory.CreateSummarizerAgent();
var titleExtractorAgent = AgentFactory.CreateTitleAgent();
var titleReviewerAgent = AgentFactory.CreateTitleReviewerAgent();
var renameManagerAgent = AgentFactory.CreateRenamingAgent();
var adminAgent = await AgentFactory.CreateAdminAsync();
var groupAdmin = await AgentFactory.CreateGroupAdminAsync();

/*
  State diagram in Mermaid syntax :
  admin --> pdfManagerAgent 
  pdfManagerAgent --> summarizerAgent 
  summarizerAgent --> titleExtractorAgent
  titleExtractor --> titleReviewerAgent
  titleReviewer --> admin
  titleReviewer --> titleExtractor
  titelReviewer --> fileManagerAgent
  fileManagerAgent --> admin
  admin --> userProxyAgent
*/
var myOrchestrator = new SpecialRolePlayOrchestrator(
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

adminAgent.SendIntroduction("I will manage the group chat, by suggesting the next speaker", groupChat);
pdfManagerAgent.SendIntroduction("I will manage the pdf extraction, which can either be a PDF with text or images", groupChat);
summarizerAgent.SendIntroduction("I will summarize text from pdf file focussing on travel details", groupChat);
titleExtractorAgent.SendIntroduction("I will generate a title from summary for the filename", groupChat);
titleReviewerAgent.SendIntroduction("I will review title from title extractor and make suggestions", groupChat);
renameManagerAgent.SendIntroduction("I will rename the file with the proposed title from titleExtractor", groupChat);
userProxyAgent.SendIntroduction("I will be the user", groupChat);

// Watch for file changes in the specified directory
void WatchDirectory(string path)
{
  FileSystemWatcher watcher = new FileSystemWatcher
  {
    Path = path,
    NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.Size,
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

