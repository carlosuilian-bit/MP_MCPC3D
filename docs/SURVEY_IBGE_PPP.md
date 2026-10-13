# Survey: IBGE-PPP Corrected Base

Date: 2026-09-14

Status: **knowledge only, nothing implemented.** No tool in this platform reads a
raw GNSS file, converts RINEX, or talks to IBGE. This document records a workflow
that already works outside the platform so a later phase can be designed against
facts instead of guesses.

## Why this belongs to the Survey module

A field crew records with an RTK collector. The base coordinate that survey is
referenced to is only as good as the base position, and a raw RTK base position
is not good enough for a georeferenced deliverable.

IBGE-PPP is the free post-processing service that fixes that: submit the base
station's raw GNSS observation, get back a precise position, and reference the
survey to it. The intended end state is that a corrected base feeds the Survey
import, so what lands in Civil 3D is already on a defensible coordinate.

That is the connection to this platform. Everything below is the upstream half,
which exists and is proven; the downstream half, importing a survey against the
corrected base, is what the operator plans to design later.

## Proven pipeline

Validated end to end outside this platform:

```text
JPS (JAVAD raw) -> RINEX 2.11 -> ZIP -> IBGE-PPP API -> processed result
```

Conversion command used:

```text
convbin -r javad -v 2.11 -od -os -o <output>.obs <input>.jps
```

- `convbin.exe` comes from RTKLIB (2.4.3 b34 in the source project).
- The generated RINEX is validated before submission.
- The RINEX is zipped before being sent; the service accepts RINEX or a ZIP
  containing RINEX.

## IBGE-PPP submission

Endpoint:

```text
https://servicodados.ibge.gov.br/api/v1/ppp
```

`POST` fields:

| Field | Meaning |
| --- | --- |
| `tipo-levantamento` | `estatico` or `cinematico` |
| `modelo-antena` | IGS/NGS antenna model string |
| `altura-antena` | vertical height to the ARP, in metres |
| `marco-ibge` | station/mark identifier |
| `email` | where the result is delivered |
| `autorizacao-uso` | use authorization flag |
| `arquivo` | RINEX, or ZIP containing RINEX |

The result is returned as a processed archive delivered per submission. In the
source project the response was written to a JSON file for inspection.

## Antenna height: the rule that matters most

**Send the vertical height from the mark to the ARP. Never the slant height.**

This is the single easiest way to produce a confidently wrong coordinate: both
numbers are present in the raw file, both look plausible, and nothing downstream
flags the mistake.

Measured evidence from the validated JAVAD file:

| Value | Number |
| --- | --- |
| Antenna model | `JAV_TRIUMPH-1   NONE` |
| Vertical height to ARP, correct for IBGE | `1.5626 m` |
| Slant height recorded in the JPS, must not be sent | `1.6200 m` |

Precedence implemented in the source script, highest first:

1. a height supplied explicitly by the operator;
2. the vertical height extracted from the raw `.jps`;
3. `ANTENNA: DELTA H/E/N` from the RINEX header.

Note the direction of that precedence: the raw file outranks the RINEX header.
Do not overwrite a trustworthy vertical height from the raw file with whatever
the RINEX header happens to carry.

## Raw format support

| Format | Vendor | Status |
| --- | --- | --- |
| `.jps` | JAVAD | Converts to RINEX 2.11 with RTKLIB `convbin`. Proven. |
| RINEX `.obs` | any | Accepted directly. |
| `.HCN` | CHC/CHCNAV | **No support.** Converted outside, by the vendor's own application. |

`.HCN` detail, recorded because it was investigated and closed:

- it is the CHCNAV raw collected-data format; a sample header identified receiver
  `CHC I73+`, antenna `CHCI73+`, height `1.8430`, `MeasuredTo 2`;
- the RTKLIB `convbin` available did not convert it to RINEX;
- `teqc` produced incoherent data and must not be used for submission;
- the operator's decision is to convert `.HCN` with the vendor software
  (CHCNAV / iGx / CGO) and feed the resulting RINEX into the flow.

So `.HCN` never reaches the platform as raw data. It arrives already converted.

## Safety rules inherited from the source work

- Only submit a valid RINEX, or a ZIP containing a valid RINEX. Never submit a
  raw file the toolchain could not convert and validate.
- Submission is explicit. In the source script nothing is sent without an
  explicit `--submit`; without it the script validates, shows the parameters and
  produces the ZIP.
- Confirm the IGS/NGS antenna model before submitting.
- Confirm the height is vertical to the ARP before submitting.
- If a new raw format is added, add its converter with its own tests before
  enabling submission for it.

## What this implies for the platform, when a phase is designed

Recorded as constraints and open questions, not as a design. The operator will
define the procedure.

Constraints this platform already imposes on such work:

- **Submitting to IBGE is an outbound network call, and this platform makes
  none today.** Every capability so far is local: named pipe restricted to the
  current user, no listening port, nothing leaving the machine. Sending survey
  observations to an external service is a new capability class, not another
  tool, and deserves an explicit decision before it is built.
- A submission cannot be rolled back. Once data reaches IBGE it has left the
  operator's control, so this belongs to the same family as a DESTRUCTIVE
  operation even though it mutates no drawing: it needs a preview, an explicit
  confirmation, and an audit record of exactly what was sent.
- The platform already has the right precedent for the first half. Phase 4B's
  `survey_fbk_dry_run` parses, reports, and touches nothing. A `dry run first,
  submit only on explicit instruction` shape matches both this platform's rules
  and the source script's `--submit` gate.
- External binaries are a new dependency shape. `convbin.exe` is not an Autodesk
  assembly and not a NuGet package; where it lives, how it is versioned, and what
  happens when it is missing all need answers before a tool depends on it.

Open questions for the operator to settle:

- Does the platform submit to IBGE at all, or does it stop at preparing a
  validated RINEX/ZIP and leave submission to a person?
- Where does the corrected base coordinate re-enter the flow: a Survey Database
  operation, a COGO point write, or a drawing-level transform?
- Which raw formats the platform accepts directly, given `.HCN` is converted
  upstream by vendor software.
- How the operator's email and use authorization are supplied without storing
  personal data in project files.

## Source

Adapted from the operator's working project at `PPP_IBGE`, which contains the
Python script, its unit tests, and the RTKLIB build that produced these results.
The pipeline was validated there against a real JAVAD file.

The operator's email address appears in that source material and in the result
URL it produced. It is deliberately not reproduced here; use `<operator-email>`
as the placeholder and supply the real address at call time.
