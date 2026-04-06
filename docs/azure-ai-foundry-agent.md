# Azure AI Foundry Agent

This application supports an `Azure AI Foundry - Agent` provider for TRM mapping lookups.

## File Search Setup

Upload the HERM TRM markdown files from `docs/trm_model/3.2` into the Azure AI Foundry agent knowledge store and enable file search for the agent.

TRM markdown files:

- [01-TRM-Domain.md](trm_model/3.2/01-TRM-Domain.md)
- [02-TRM-Capability.md](trm_model/3.2/02-TRM-Capability.md)
- [03-TRM-Component.md](trm_model/3.2/03-TRM-Component.md)
- [HERM-TRM-V320-explainer.md](trm_model/3.2/HERM-TRM-V320-explainer.md)
- [TRM-LLM-Instructions-v2.md](trm_model/3.2/TRM-LLM-Instructions-v2.md)

## Agent Instructions

Use these instructions for the Azure AI Foundry agent:

```text
Use the attached HERM Technology Reference Model as the only allowed taxonomy.

Then map all the capabilities, primary and secondary, to the files attached
```

## Provider Defaults

Use this system prompt in the `Azure AI Foundry - Agent` provider configuration:

```text
Never invent component ids, codes, or names.
Return only the requested output format with no extra prose.
```

Use this query / prompt template in the `Azure AI Foundry - Agent` provider configuration:

```text
Return all TRM mappings for {{VendorProduct}}.

Requirements:
- Use only the TRM catalogue provided below.
- Do not invent TRM component codes or names.
- Return JSON only, with no markdown fence and no prose before or after the JSON object.
- Use this JSON shape:
{
  "summary": "one short sentence",
  "mappings": [
    {
      "component_code": "TC001",
      "component_name": "Directory Service",
      "confidence": 0.95,
      "reason": "one short sentence"
    }
  ]
}
- confidence must be a number between 0 and 1.

product:
  name: {{ProductNameToon}}
  vendor: {{VendorToon}}
  description: {{ProductDescriptionToon}}

{{ExistingMappingsBlock}}

{{TrmComponentsBlock}}
```
