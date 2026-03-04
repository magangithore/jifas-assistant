# Copilot Instructions

## General Guidelines
- First general instruction
- Second general instruction

## Knowledge Base Management
- Prefer a chunking-based approach for Knowledge Base loading.
- Split the Knowledge Base into semantic chunks (~500 characters with a 50-character overlap) before generating embeddings to improve RAG accuracy over full-document embeddings.
- Implementation steps:
  - Bulk insert full documents first.
  - Generate document-level embeddings.
  - Chunk documents.
  - Generate chunk embeddings.
- Ensure all steps are executed in a single script execution. 

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- Custom requirement A
- Custom requirement B