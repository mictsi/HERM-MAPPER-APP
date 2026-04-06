# AI Guide

## Overview

The application supports AI-assisted product-to-TRM mapping through configurable providers in the admin UI.

Provider configuration supports:

- provider-specific endpoints
- model, deployment, or agent names
- API keys
- timeout settings
- provider-specific system prompts
- provider-specific query / prompt templates

## Current AI Features

- Configurable AI providers
- Inline provider editing in the admin UI
- Saved provider-specific prompts
- Azure AI Foundry Agent support
- Usage logging for prompt/completion tokens and estimated cost

## Azure AI Foundry Agent

For the Foundry Agent setup, uploaded TRM markdown files, file-search guidance, and recommended prompt defaults, see:

- [Azure AI Foundry Agent Setup](azure-ai-foundry-agent.md)

## TRM Markdown Files

The markdown source for the HERM TRM reference content lives under:

- [docs/trm_model/3.2](trm_model/3.2/)

Files:

- [01-TRM-Domain.md](trm_model/3.2/01-TRM-Domain.md)
- [02-TRM-Capability.md](trm_model/3.2/02-TRM-Capability.md)
- [03-TRM-Component.md](trm_model/3.2/03-TRM-Component.md)
- [HERM-TRM-V320-explainer.md](trm_model/3.2/HERM-TRM-V320-explainer.md)
- [TRM-LLM-Instructions-v2.md](trm_model/3.2/TRM-LLM-Instructions-v2.md)

## Related Docs

- [Configuration Guide](configuration.md)
- [Azure AI Foundry Agent Setup](azure-ai-foundry-agent.md)
