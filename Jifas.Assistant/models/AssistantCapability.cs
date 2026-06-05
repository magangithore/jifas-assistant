using System.Collections.Generic;

namespace Jifas.Assistant.Models
{
    /// <summary>
    /// Metadata kemampuan assistant untuk UI, dokumentasi, dan endpoint capability.
    /// </summary>
    public class AssistantCapability
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Examples { get; set; } = new List<string>();
    }
}
