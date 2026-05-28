using System.Collections.Generic;

namespace Aimitra.WebChat.Models
{
    public class KernelFunctionOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string FunctionType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class AgentDefinition
    {
        public string Name { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
        public string Description { get; set; } = string.Empty;
        public string WelcomeMessage { get; set; } = string.Empty;
        public List<TopicDefinition> Topics { get; set; } = new();
        public List<AutonomousAgentDefinition> AutonomousAgents { get; set; } = new();
    }

    public class TopicDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> KernelFunctions { get; set; } = new();
    }

    public class AutonomousAgentDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string TriggerType { get; set; } = "Time";
        public string TriggerValue { get; set; } = string.Empty;
        public string ActionType { get; set; } = "RunTopic";
        public string ActionValue { get; set; } = string.Empty;
        public string Schedule { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }
}
