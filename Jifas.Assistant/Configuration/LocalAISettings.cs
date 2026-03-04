namespace Jifas.Assistant.Configuration
{
    /// <summary>
    /// Configuration for Local AI Service (Ollama or compatible)
    /// </summary>
    public class LocalAISettings
    {
        /// <summary>
        /// Base URL of local AI server
        /// Default: http://10.0.12.54:11434
        /// </summary>
        public string BaseUrl { get; set; } = "http://10.0.12.54:11434";

        /// <summary>
        /// Model name to use
        /// Default: qwen3:8b
        /// </summary>
        public string Model { get; set; } = "qwen3:8b";

        /// <summary>
        /// Temperature for response generation (0-2)
        /// Lower = more deterministic, Higher = more creative
        /// Default: 0.7
        /// </summary>
        public float Temperature { get; set; } = 0.7f;

        /// <summary>
        /// Top-p for nucleus sampling (0-1)
        /// Default: 0.9
        /// </summary>
        public float TopP { get; set; } = 0.9f;

        /// <summary>
        /// Top-k for top-k sampling
        /// Default: 40
        /// </summary>
        public int TopK { get; set; } = 40;

        /// <summary>
        /// Request timeout in seconds
        /// Default: 30
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;
    }
}
