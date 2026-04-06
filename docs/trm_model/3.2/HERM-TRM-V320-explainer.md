# HERM Technology Reference Model Explainer

## Introduction to the Technology Reference Model

The Technology Reference Model (TRM) was introduced in HERM Version 3.1.0 to provide an industry-agnostic model of technology capabilities and technology components to underpin the business, application, and data domains. The TRM is a response to community feedback that having a technology classification model would be useful. The TRM acts as an accelerator for those building or redefining their digital estate and the technology portfolio within it, and supports a wide range of enterprise-architecture use cases.

## Model Anatomy

### Model Context

The TRM concept is long-standing and referenced in many Enterprise Architecture frameworks. The HERM TRM was established by analysing several existing models to generate initial working versions and refined subsequently by the HERM Working Group and substantial community review to create the first release.

It is important to note that the HERM Application Reference Model (ARM) and the TRM are interdependent, and must be used together to provide a complete model of an institution’s overall digital landscape. The relationship between applications and technologies can be most-simply described as:

- Applications provide functionality that supports specific business processes.
- Technologies support applications and other technologies.

Following this approach, the ARM and TRM are separate models, as their taxonomy and categorisation are fundamentally different. Applications are grouped according to their relationship with business capabilities, whereas technologies are grouped according to their primary functionality.

Please refer to the HERM Application Reference Model Explainer for further explanation of how the categorisation assignments between applications and technologies have been made.

## Model Structure

The TRM consists of three primary elements. Technology Components are grouped into Technology Capabilities, and further grouped into Technology Domains, as detailed below:

- **Technology Domain**: The top-level categorisation of technology elements (e.g., "Data & Information").
- **Technology Capability**: More-granular collections of similarly-capable technology services and things (e.g., "Data Repository").
- **Technology Component**: Discrete elements of technology features and behaviours that provide a unique logical component and can be mapped to a specific technology instance (e.g., "Data Lake").

### Model Attributes

- **Technology Domain | Capability | Component Code**: A unique identifier for each model element, in the form `TD###` for Technology Domains, `TP###` for Technology Capabilities, and `TC###` for Technology Components. These codes are never reassigned or recycled.
- **Technology Domain | Capability | Component Name**: The name of the TRM model element.
- **Technology Capability | Component Parent Capability**: The name of the parent Technology Domain (for Technology Capabilities) or the parent Technology Capability (for Technology Components).
- **Technology Domain | Capability | Component Description**: A brief description of the unique scope of each TRM model element.
- **Technology Domain | Capability | Component Comments**: Additional explanatory descriptive commentary on the nature of the TRM model element.
- **Technology Product Examples**: Currently-available products that are examples of the specific Technology Component being defined. The Technology Product Examples provided in the catalogue are offered on a purely-illustrative basis that is intended to guide HERM users to better understand and use the TRM and its Technology Components in a higher-education context. Particularly:
  - inclusion as an example implies neither endorsement nor recommendation.
  - examples given are never intended to be an exhaustive list nor a "buyer's guide".
  - community feedback ensures the examples offered are refreshed periodically.

## Design Principles

The design principles that have guided the specification of the TRM include those brought to bear across the whole of the HERM, intersections with those applicable to specification of the ARM, and other examples including:

- **Technology Services**: In general, the TRM is intended to be a self-similar taxonomy of Technology Services, rather than layered collections of different elements. These Technology Services are modelled independently from their specific deployment alternatives (e.g., cloud, on-premises, embedded systems). The TRM will become gradually consistent as a taxonomy of technology services that is generalised and abstracted to a level that makes sense and is able to provide enduring value. The intention is to work at a level that ensures the TRM and its elements are relatively stable year-on-year as the underlying implementations change.
- **Purpose-Agnostic**: Being positioned as a collection of technology services, the TRM is positioned as an industry-agnostic reference model designed to stand alongside and complement the ARM and the other domains comprising the HERM. For that reason, the TRM includes elements such as Advanced Computation that may be (and are often) used to enable research activity, but, as this is not the only purpose to which such technologies may be applied, and as this is not the entire scope of research-enabling technologies, there is no specific naming or suggestion here of "Research Computing".
- **Market-Led**: TRM elements are typically evidenced by in-market solutions that have shaped the identification and categorisation of the technology services represented in the model. As marketplace offerings evolve and consolidate, so too will the TRM, though with the expectation that there will be relatively greater effects seen in the Technology Component layer than at higher layers of the model.

## Artificial Intelligence

The AI-related elements within the Technology Reference Model are subject to heightened volatility and more frequent updates than other components. This reflects the rapidly evolving nature of artificial intelligence technologies, standards, and best practices in higher education.

Given the dynamic state of AI technologies, institutions using the TRM should:

- Review AI entities more frequently. We recommend quarterly reviews of AI-related elements, compared to annual reviews for more stable technology entities.
- Maintain flexibility in AI implementations. Architecture and procurement decisions involving AI and framing using the TRM should incorporate greater adaptability to accommodate emerging capabilities and shifting vendor landscapes.
- Supplement with current research. Consult recent industry publications, vendor announcements, and peer institution experiences when making AI-related decisions, as the TRM may not reflect developments from the past few months.
- Participate in updates. We welcome feedback on AI components to inform future revisions of the TRM.

## Use-Cases for the TRM

Many primary use-cases are enabled by having a TRM, including:

- Mapping your technology estate to the TRM to enable visibility that informs governance, identifies gaps, and highlights potential opportunities for consolidation and rationalisation.
- Scoping the technology footprint to inform strategic decision-making and prepare for transformation (platforms, cloud, etc).
- Assigning technology fitness, value, and cost as inputs to defining technology standards that lead to greater fleet reusability and lower technical debt.
- Matching effort, cost, and staffing and skills to the technology estate in order to understand Total Cost of Ownership and workforce skills gaps and gluts.
- Overlaying current-state and expected-state Recovery Time Objective and Recovery Point Objective across the technology estate to enable business continuity planning.
- Modelling and performing what-if analysis of Environmental Sustainability performance across the technology estate.

A number of use-cases for the TRM are also being documented in the HERM use-case compendium.

## How to Get Started

Mapping your digital estate into the TRM and the ARM is a good way to get started, thinking during the process of doing that about the lifecycle status, fitness and value, supportability, sustainability, and business criticality of each mapped element.

## Recommended Practices

- When using the TRM, work hand-in-hand with the Application Reference Model.
- The fast-paced change attributable to technological advancements and the appearance and consolidation of products and services affects the TRM to a greater extent than the other HERM domains. Consequently, it may be necessary to amend and augment the TRM to cater for new and emerging technologies of particular importance to your institution.
- Think just-enough about current-state architecture and further about your desired future-state and about the various transition states required to get there.
- Share your TRM feedback, questions, challenges, and successes with the HERM community.

## Providing Feedback

If you have any feedback or suggestions about the Higher Education Reference Models, please collaborate directly with your local Enterprise Architecture Special Interest Group, or contact the HERM Working Group directly at `herm-feedback@googlegroups.com`.
