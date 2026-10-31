# Local AI setup

NEXUS PC connects only to Ollama at `http://127.0.0.1:11434`. Install Ollama from [ollama.com/download](https://ollama.com/download) if it is not available. Model downloads require internet access; inference after download runs on the local machine. NEXUS PC does not upload system reports or conversation history to a cloud AI service.

The Local AI window lists installed models, streams Ollama pull status, supports cancellation, confirms deletion, and can send a local chat request. Model storage location follows Ollama's own configuration and is not changed by NEXUS PC.

Chat history is stored locally per model under `%APPDATA%\NEXUSPC\Chats`; the Local AI window can clear history for the selected model after confirmation. Before installing a custom model, verify its size, intended use, and license with the model publisher; custom model IDs are passed to Ollama as provided.
