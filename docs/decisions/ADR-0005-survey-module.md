# ADR-0005: Survey Module

Date: 2026-09-04

Status: Accepted

## Context

Survey workflows are central to the target product. `KevinGriffin-new/civil3d-mcp` has the strongest local evidence for:

- Survey working folders
- Survey projects
- Survey networks
- control and non-control points
- setups and observations
- figures
- Figure Prefix Databases
- FBK parsing/import
- Survey COM quirks in Civil 3D 2026

The implementation is valuable but monolithic and includes broad diagnostic escape hatches.

## Decision

Build Survey as a separate module with interfaces and adapters. Do not put Survey COM logic in the core dispatcher.

Initial module boundaries:

- `IFbkParser`
- `ISurveyGateway`
- `ISurveyProjectRepository`
- `ISurveyNetworkRepository`
- `ISurveyPointRepository`
- `ISurveyObservationRepository`
- `IFigurePrefixRepository`

Start read-only where possible, then add safe writes with dry-run previews.

## Alternatives

1. Copy the KevinGriffin bridge into the core.
   - Rejected. It is too monolithic and has too many broad escape hatches.

2. Delay Survey until after all generic Civil 3D tools.
   - Rejected. Survey is one of the core differentiators of this platform.

3. Use only AutoCAD/Civil 3D .NET APIs and avoid COM.
   - Rejected as an absolute rule. Survey database operations appear to require COM or command fallback in important cases.

## Consequences

Positive:

- Survey-specific complexity stays contained
- COM quirks can be tested behind adapters
- FBK dry-run can be unit-tested without Civil 3D
- high-value topography workflows can ship without destabilizing the core

Negative:

- early-bound COM references may complicate build and deployment
- not every Survey operation can have normal AutoCAD transaction rollback
- some imports may need asynchronous polling and explicit completion status
