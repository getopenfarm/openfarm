# Documentation writing guide

Write for print-farm owners and operators. Use clear technical English guided
by ASD-STE100 principles. This is an OpenFarm writing policy, not a claim of full
ASD-STE100 compliance.

The [official ASD-STE100 FAQ](https://asd-ste100.org/STE_faq.html) describes its
writing rules, controlled vocabulary, and permitted technical terminology. This
guide adopts its clarity principles. We do not claim to have checked every word
against the standard's dictionary.

## Audience and technical detail

- Assume the reader understands ordinary 3D printing work. Keep terms such as
  filament, infill, nozzle diameter, build plate, G-code, slicer, and AMS.
- Explain OpenFarm-specific terms when first needed. Link to Key concepts for
  definitions such as slicer pin, production snapshot, and dispatch.
- Keep exact software terms in setup and API guides: Docker Compose, PostgreSQL,
  environment variable, HTTP, WebSocket, and personal access token. Explain the
  action the reader needs to take and the result to expect.
- Put implementation details in `dev-notes/` unless readers need them to configure
  the farm or understand a limitation.

## Sentences and paragraphs

- Use active voice and name the actor. Distinguish what the user does from what
  OpenFarm or the printer does.
- Put one main idea in each sentence. Aim for about 20 words in instructions and
  25 in explanations. These are editing targets, not a compliance test.
- Put a condition before the action it controls: "If the printer is idle, select…"
- Keep paragraphs focused on one topic. State the behavior before exceptions.
- Replace noun chains and slash-separated phrases with complete wording. For
  example, write "printer model and nozzle size" instead of "model/nozzle group."
- Use familiar verbs. Prefer "use," "save," "start," and "send" to more abstract
  wording when they have the same meaning.
- Retain a necessary exception even if it makes a sentence or paragraph longer.
  Split the explanation rather than removing the condition.

## Procedures

- Use numbered steps when order matters. Start user actions with an imperative
  verb, such as "Open," "Select," "Enter," or "Save."
- Give each step one main action. Keep a short condition or explanation with its
  action when separating it would make the procedure harder to follow.
- Keep automatic behavior separate from user steps. A description of what
  OpenFarm does must not read like work the reader must perform.
- Explain prerequisites before the command or action. Place destructive-action
  warnings before the affected operation.
- State the expected result and the next step when a failure requires user action.

## Terminology

Use one term consistently for each concept:

| Term | Usage |
| --- | --- |
| Profile or preset | Saved slicer settings. Explain once that the terms refer to the same kind of settings. Use the interface's profile labels. |
| Slicer pin | A part's saved slicer engine and exact version. Do not confuse it with pinning a job to a printer. |
| Pinned printer | The specific printer a job is allowed to use. |
| Matching preset | A preset that satisfies the documented identity and compatibility rules. Avoid unexplained "native twin." |
| Production snapshot | The production inputs saved for a job. It does not freeze live printer configuration or reserve stock. |
| Saved job file | The execution file prepared and kept for a job. Use "execution artifact" only where its implementation meaning is needed. |
| Printer report | Use this in operator instructions when "telemetry" adds no useful detail. Retain telemetry in protocol explanations. |
| Bill of materials (BOM) | An assembly's components and quantities. Expand the abbreviation at first use on a page. |
| Dispatch | Preparing and sending a job to a printer. Explain this term where necessary. |

Use "must" for requirements, "can" for capabilities, and "might" for uncertain
outcomes. Preserve the distinction between required configuration and a recommendation.

## Interface labels and literal examples

- Copy interface labels exactly and format them in bold. Use backticks for paths,
  field names, commands, filenames, and literal values.
- Do not simplify command syntax, environment variable names, API routes, ports,
  units, firmware requirements, or error meanings during an editorial change.
- Keep code blocks executable or clearly identify placeholders. Do not change
  a command to meet a sentence-length target.
- Use tables for file behavior and configuration comparisons. Keep each cell
  readable and retain its conditions.
- Explain diagrams in surrounding text. Give images meaningful alternative text.
- Keep existing heading anchors stable when possible. Verify links after changes.

## Review before submission

1. Read every changed page for meaning, not only sentence length.
2. Verify that technical terms are consistent and unfamiliar terms are explained.
3. Compare procedures with the current interface and implementation.
4. Check that commands, literals, warnings, requirements, and exceptions retain
   their original meaning.
5. Run the docs typecheck and production build described in `README.md`.

Automated readability checks can identify wording to review. They do not prove
technical accuracy or ASD-STE100 compliance. Follow the repository's `AGENTS.md`
instructions for visual verification.
