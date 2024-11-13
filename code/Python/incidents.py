import json
import os, datetime
import agentops
from pprint import pprint
from dotenv import load_dotenv
from typing import Annotated, Literal
import autogen
from autogen import AssistantAgent, UserProxyAgent, ConversableAgent

# Load the environment variables
load_dotenv()

#agentops.init(os.environ["AGENTOPS_KEY"])

llm_config = autogen.config_list_from_json(env_or_file="llm_config.json")[0]
llm_config["api_key"] = os.environ["AZURE_OPENAI_API_KEY"]
llm_config["cache_seed"] = None

# read all files in folder data and add their content to a list
def read_data(datatype: Annotated[str,"type of datasource"]) -> str:
    folder = "data/"
    files = os.listdir(folder)
    print(f"Data type: {datatype}")
    
    content = []
    for file in files:
        with open(folder + file, "r") as f:
            content.append(f.read())
#return a string with all the content
    strContent = [x for x in content if x != ""]  # remove empty strings
    return strContent


agent_incident = UserProxyAgent(
    name="agent_incident",
    # system_message="You are an AI assistant, that takes IT incidences as input and orchestrates the resolution of the incident. "
    #                 "You go from incident classification, to Root Cause Analysis, to midiation, to post mortem reporting. ",
    code_execution_config={
    "use_docker": False,
    },
    is_termination_msg=lambda msg: msg.get("content") is not None and "TERMINATE" in msg["content"],
)

agent_incident_class = ConversableAgent(
    "agent_incident_class",
    system_message="""You are an AI assistant that helps working with IT incidents.
                        The following is your scope:
                        Classify the following incidents based on severity and type.

                        Example 1:
                        Incident: 'Server CPU usage is high.'
                        Classification: 'High severity, Performance issue.'

                        Example 2:
                        Incident: 'Database connection timeout.'
                        Classification: 'Medium severity, Connectivity issue.'

                        Example 3:
                        Incident: 'Disk space running low on server.'
                        Classification: 'Low severity, Storage issue.'

                        Example 4:
                        Incident: 'Memory leak detected in application.'
                        Classification: 'High severity, Application issue.'""",
    llm_config=llm_config,
    human_input_mode="NEVER",  # never ask for human input
)

agent_mitigation = ConversableAgent(
    "agent_mitigation",
    system_message="""You are an AI assistant, that mitigates the issue based on the incident.
You will lead a team of it experts, by providing a step-by-step plan to work through the possible IT components to reduce the issues impacts.
You can access the network logs, system logs, and other data that is stored, to gain more insights on what happened.
Afterwards, you create a mitigation report for a following root cause analysis.""",
    llm_config=llm_config,
    max_consecutive_auto_reply=3,
    human_input_mode="NEVER",
)

agent_it_expert = ConversableAgent(
    "agent_it_expert",
    system_message="""You are an AI IT Pro, very expert in IT infrastructure. You know the infrastructure architecture and it's components. In case mitigation agent want's you to investigate an issue on an infra component, you know what to do and know how to resolve.
You will understand what to do and how to mitigate it. You respond to the request of the mitigation agent, what you understood and what you did to resolve or mitigate. """,
    llm_config=llm_config,
    human_input_mode="NEVER",
)   

agent_RCA = ConversableAgent(
    "agent_RCA",
    system_message="""You are an AI assistant, that does Root Cause Analysis. You work closely with it experts and mitigation agent.
Your task is to identify the root cause of the incident.
You can access all network logs, system logs, and other stored data.
After analysing, you return with a formal RCA report. and say TERMINATE when done.""",
    llm_config=llm_config,
    max_consecutive_auto_reply=1,
    human_input_mode="NEVER",
)

agent_executor = ConversableAgent(
    "agent_executor",
    system_message="""You are an AI assistant, that executes the code and reports the results.""",
    llm_config=False,
    human_input_mode="NEVER",
    code_execution_config={
        "use_docker": False,
    },
    
)


# register function to read files in folder data
agent_executor.register_for_execution(name="read_data")(read_data)
agent_RCA.register_for_llm(name="read_data",description="gets the data from the storage for the searched data type (network, firmeware etc.)")(read_data)
agent_mitigation.register_for_llm(name="read_data",description="gets the data from the storage for the searched data type (network, firmeware etc.)")(read_data)

print("Initiating chat...")
today = datetime.datetime.now().strftime("%Y-%m-%d")
print(f"{today}: What is the incident about:")

# read incident.txt into a variable
with open("incident.txt", "r") as file:
    incidentData = file.read()

with open("mitigation_report.txt", "r") as file:
    mitigation_report = file.read()

result = autogen.GroupChat(
    agents=[agent_incident_class, agent_executor, agent_mitigation, agent_RCA],
    messages=[],
    max_round=10,
    speaker_selection_method="auto"
)
manager = autogen.GroupChatManager(groupchat=result, llm_config=llm_config)
agent_incident.initiate_chat(manager, message=incidentData, summary_method="reflection_with_llm")

# result = agent_incident.initiate_chats(
#     [
#         {
#             "recipient": agent_incident_class,
#             "message": read,
#             "max_turns": 1,
#             "summary_method": "reflection_with_llm",
#         },
#         {
#             "recipient": agent_mitigation,
#             "message": f"use the incident and its RCA to create a mitigation runbook. and use the following mitigation report.Today is {today}.",
#             "max_turns": 1,
#             "summary_method": "reflection_with_llm",
#         },
#         {
#             "recipient": agent_RCA,
#             "message": "Look through the incident and the data logs (network, firmware, tickets) to create a root cause analysis.",
#             "max_turns": 1,
#             "summary_method": "reflection_with_llm",
#         }
#     ]
# )

