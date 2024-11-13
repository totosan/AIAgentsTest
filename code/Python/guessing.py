import os
from dotenv import load_dotenv
import autogen
from autogen import AssistantAgent, UserProxyAgent, ConversableAgent

# Load the environment variables
load_dotenv()
llm_config = autogen.config_list_from_json(env_or_file="llm_config.json")[0]
llm_config["api_key"] = os.environ['AZURE_OPENAI_API_KEY']

agent_with_number = ConversableAgent(
    "agent_with_number",
    system_message="You are playing a game of guess-my-number. You have the "
    "number 53 in your mind, and we will try to guess it. We are multiple players and being asked each after another."
    "If I guess too high, say 'too high', if I guess too low, say 'too low'. ",
    llm_config={"config_list": [{"model": "gpt-4", "api_key": os.environ["OPENAI_API_KEY"]}]},
    is_termination_msg=lambda msg: "53" in msg["content"],  # terminate if the number is guessed by the other agent
    human_input_mode="NEVER",  # never ask for human input
)

agent_guess_number = ConversableAgent(
    "agent_guess_number",
    system_message="I have a number in my mind, and you will try to guess it. "
    "If I say 'too high', you should guess a lower number. If I say 'too low', "
    "you should guess a higher number. ",
    llm_config={"config_list": [{"model": "gpt-4", "api_key": os.environ["OPENAI_API_KEY"]}]},
    human_input_mode="NEVER",
)


result = agent_with_number.initiate_chat(
    agent_guess_number,
    message="I have a number between 1 and 100. Guess it!",
    summary_method="reflection_with_llm",
)

print(result.summary)
