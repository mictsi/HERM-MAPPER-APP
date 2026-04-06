# LLM Instructions for the HERM Technology Reference Model (TRM)

## Purpose
Use the HERM Technology Reference Model (TRM) as a classification model for **technology services and technology components** in higher education. The TRM is intended to help describe, inspect, rationalise, and plan a technology estate.

Do **not** use the TRM as an application taxonomy. Applications belong in the HERM Application Reference Model (ARM). The TRM and ARM are complementary and should be used together when a full digital-estate view is required.

## Core Model Structure
The TRM has three levels:

1. **Technology Domain**
   - Top-level grouping of technology areas.
   - Code pattern: `TD###`
   - Example: `TD003 Data & Information`

2. **Technology Capability**
   - A more specific grouping of similar technology services.
   - Code pattern: `TP###`
   - Example: `TP013 Data Repository`

3. **Technology Component**
   - The most specific logical technology element.
   - Code pattern: `TC###`
   - Example: `TC034 Data Lake`

Hierarchy rule:
- A **Technology Component** belongs to one **Technology Capability**.
- A **Technology Capability** belongs to one **Technology Domain**.

## What the TRM Represents
Treat the TRM as a taxonomy of **technology services**, not a list of vendors, products, cloud services, or deployment patterns.

Classification should be based on the **functions the technology materially provides**, not just the vendor label or bundled packaging.

Use these rules:
- Identify the **primary function** of the technology and return its best-fit TRM mapping.
- Also identify any **secondary functions** that are material, distinct, and intentionally delivered by the technology.
- Return **multiple TRM components** where the product genuinely spans multiple technology services.
- Do **not** add extra mappings for incidental, minor, or marketing-only features.

Examples:
- A data lake maps to `TC034 Data Lake` under `TP013 Data Repository` in `TD003 Data & Information`.
- Email maps to `TC048 Email` under `TP026 Communication` in `TD008 Communication & Collaboration`.
- Single sign-on maps to `TC111 Single Sign-On` under `TP022 Authentication` in `TD007 Digital Identity`.

## Classification Rules for an LLM
When asked to classify a product, platform, service, or capability:

1. **Identify whether the subject is a technology or an application.**
   - If it is primarily an application/business-function classification problem, say the ARM may be a better primary model.
   - If it is infrastructure, platform, middleware, data, identity, security, or operational technology, use the TRM.

2. **Classify by function, not vendor packaging.**
   - Ignore branding and bundled SKU names.
   - Focus on the services the technology actually provides.

3. **Always identify the primary function first.**
   - Return the best-fit Domain, Capability, and Component for the technology's main role.
   - This is the anchor mapping.

4. **Also evaluate secondary functions.**
   - Check whether the technology provides additional functions that are material, distinct, and meaningful in architecture, operations, procurement, or service design.
   - If yes, return additional TRM mappings for those secondary functions.
   - If no, return only the primary mapping.

5. **Choose the most specific Technology Component possible for each function.**
   - Prefer a `TC###` match when one clearly exists.
   - If no component is certain, fall back to the best-fit capability (`TP###`) and explain the uncertainty.

6. **Support many-to-many mapping when needed.**
   - A single product may span multiple components.
   - Map each materially distinct function separately instead of forcing one inaccurate category.
   - Distinguish between core functions, secondary functions, and incidental features.

7. **Return multiple components when secondary functions justify them.**
   - Do not force the answer into a single component if the technology clearly spans several.
   - Include all justified component mappings in the response, clearly separating primary and secondary mappings.
   - Where multiple secondary functions exist, return them all if they are materially relevant.

8. **Do not classify by deployment model.**
   - Cloud, on-premises, SaaS, managed service, and embedded deployment choices do not change the logical classification.

9. **Do not treat example products as endorsements or an exhaustive list.**
   - Product examples in the catalogue are illustrative only.

10. **Preserve the hierarchy in answers.**
   - When possible, return Domain, Capability, and Component together for every mapping.

## Functional Mapping Decision Rule
Use this decision test for every candidate mapping:

Include a mapping when the function is:
- a deliberate and recognised part of the technology
- material to how the technology is selected, governed, operated, or integrated
- distinct enough to warrant its own TRM component

Exclude a mapping when the function is:
- incidental
- lightweight or supporting-only
- merely adjacent marketing language
- not clear enough to assign confidently

## Preferred Answer Format
When classifying a technology, use this structure:

```text
TRM Classification

Primary TRM Mapping
- Domain: TD### <name>
- Capability: TP### <name>
- Component: TC### <name>

Secondary TRM Mapping(s)
- Domain: TD### <name>
  Capability: TP### <name>
  Component: TC### <name>
- Domain: TD### <name>
  Capability: TP### <name>
  Component: TC### <name>

Reasoning
- Explain why the primary mapping is the best fit.
- Explain why each secondary mapping is materially justified.
- Note any ambiguity or capability-level fallback.

Confidence
- High | Medium | Low
```

If the technology has only one justified mapping, still use the same structure and state:

```text
Secondary TRM Mapping(s)
- None identified
```

If the subject maps to more than one TRM component, return **multiple components** based on both the primary function and the materially distinct secondary functions.

## Model Attributes to Preserve
Where available, preserve these fields from the catalogue:
- Code
- Name
- Parent domain or parent capability
- Description
- Comments
- Product examples

Do not invent missing descriptions or codes.

## Important Interpretation Guidance

### 1. TRM vs ARM
Use the TRM for technologies that support applications and other technologies.
Use the ARM for applications that primarily deliver business functionality.

### 2. Service-Oriented Interpretation
Interpret TRM elements as logical services or capabilities. Avoid anchoring classification to a specific implementation detail.

### 3. Stability by Layer
The model is generally more stable at the Domain and Capability levels than at the Component level.
If a precise component is unclear, a capability-level answer may be more durable.

### 4. Primary and Secondary Function Handling
The primary mapping should reflect the technology's main role.
Secondary mappings should reflect additional material services the same technology provides.
A secondary mapping is not optional when the technology clearly delivers another distinct service that matters architecturally.

### 5. AI Volatility
AI-related parts of the TRM are explicitly more volatile than the rest of the model.
For AI technologies:
- prefer current validation before making procurement or architecture recommendations
- expect more frequent taxonomy change
- treat recent market developments as potentially ahead of the catalogue
- note that quarterly review is recommended for AI-related entities

Relevant AI capability and components include:
- `TP032 Artificial Intelligence`
- `TC147 Natural Language Processing`
- `TC148 Conversational Artificial Intelligence`
- `TC149 Machine Perception`
- `TC150 Machine Learning`
- `TC151 Generative Artificial Intelligence`
- `TC153 Artificial Intelligence Agent`
- `TC135 Artificial Intelligence Governance`

## When an LLM Should Escalate Uncertainty
State uncertainty explicitly when:
- the technology spans multiple TRM components equally
- the input is too vague
- the subject is more application-like than technology-like
- the catalogue does not provide a clear component
- the subject is a rapidly changing AI technology

Use phrases such as:
- "Best-fit TRM component"
- "Capability-level match only"
- "Secondary mapping may also apply"
- "This should be validated against the current catalogue"

## Examples

### Example 1: Data Lake platform
```text
TRM Classification

Primary TRM Mapping
- Domain: TD003 Data & Information
- Capability: TP013 Data Repository
- Component: TC034 Data Lake

Secondary TRM Mapping(s)
- None identified

Reasoning
- The technology's main role is storing large-scale data for analytical use.
- No additional material secondary function is evidenced in the input.

Confidence
- High
```

### Example 2: Enterprise SSO service
```text
TRM Classification

Primary TRM Mapping
- Domain: TD007 Digital Identity
- Capability: TP022 Authentication
- Component: TC111 Single Sign-On

Secondary TRM Mapping(s)
- None identified

Reasoning
- The service primarily provides authentication session continuity across systems.
- No additional materially distinct secondary function is clear from the input.

Confidence
- High
```

### Example 3: Collaboration suite with chat, meetings, and email
```text
TRM Classification

Primary TRM Mapping
- Domain: TD008 Communication & Collaboration
- Capability: TP025 Collaboration
- Component: TC015 Collaboration Platform

Secondary TRM Mapping(s)
- Domain: TD008 Communication & Collaboration
  Capability: TP026 Communication
  Component: TC064 Instant Messaging
- Domain: TD008 Communication & Collaboration
  Capability: TP026 Communication
  Component: TC048 Email
- Domain: TD008 Communication & Collaboration
  Capability: TP026 Communication
  Component: TC146 Unified Communications

Reasoning
- The product's main role is a collaboration platform.
- It also provides materially distinct communication functions, so multiple secondary component mappings are required.

Confidence
- High
```

### Example 4: Identity platform with SSO and MFA
```text
TRM Classification

Primary TRM Mapping
- Domain: TD007 Digital Identity
- Capability: TP022 Authentication
- Component: TC111 Single Sign-On

Secondary TRM Mapping(s)
- Domain: TD007 Digital Identity
  Capability: TP022 Authentication
  Component: <best-fit MFA-related component if present in catalogue>

Reasoning
- SSO is the primary functional anchor.
- MFA is a separate material authentication function and should also be returned if supported by the catalogue.

Confidence
- Medium
```

## What Not to Do
- Do not confuse technologies with business capabilities.
- Do not classify solely by vendor name.
- Do not assume product examples are recommended standards.
- Do not collapse multiple major functions into one component if the product clearly spans several.
- Do not ignore justified secondary mappings.
- Do not ignore the hierarchy.
- Do not give AI classifications without caveating model volatility when the decision is material.

## Minimal Operating Rule
If uncertain, return the **best-fit capability** and explain why a lower-level component cannot be assigned confidently.
If multiple materially distinct secondary functions are present, return **multiple components** rather than suppressing them.
