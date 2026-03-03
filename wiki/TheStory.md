# ZeroMcp: The Story

## TL;DR;
ZeroMcp turns your existing ASP.NET Core API into a complete AI toolset with zero duplication, zero ceremony, and zero friction — instantly; because I didn’t want to do all of that boilerplate legwork either.


## Why ZeroMcp Exists
ZeroMcp wasn’t created as a research experiment or a side‑project. It was born out of a mandate — a top‑down directive to “make a very large, very real system AI‑enabled.” The system already had hundreds of ASP.NET Core endpoints, models, filters, and behaviours. Rewriting all of that as MCP tools would have meant duplicating every route, every schema, every validation rule, every description, every example.

That duplication felt wrong. It felt fragile. It felt like work a machine should do, not a developer.

There should have been a tool that exposed an existing API to AI agents without rewriting anything.  
There wasn’t.  
So ZeroMcp was created.

It started as a practical solution to avoid duplicating a massive codebase — and became a framework that solves the same problem for everyone else.

---

## The shift from APIs to AI tools
APIs have been the backbone of software integration for decades. REST, JSON, and OpenAPI defined how machines communicated. But AI agents don’t consume APIs the way traditional systems do. They need tools: structured, self‑describing operations with clear inputs, outputs, examples, and intent. They need metadata, not routes. They need meaning, not endpoints.

The Model Context Protocol (MCP) bridges this gap, giving AI agents a way to discover, understand, and safely execute capabilities exposed by your application. But MCP introduces a new challenge: exposing existing APIs without rewriting them.

---

## The duplication problem
If you already have an ASP.NET Core API, exposing it to MCP should be simple. Without ZeroMcp, it isn’t. Developers must rewrite endpoints as MCP tools, duplicate request and response models, manually craft schemas, maintain metadata, and keep everything in sync as the API evolves. This mirrors the pre‑Newtonsoft.Json era, where too much ceremony and duplication slowed development.

ZeroMcp solves this problem for MCP the same way Newtonsoft solved it for JSON.

---

## Your API becomes an AI toolset instantly
ZeroMcp takes your existing ASP.NET Core application — controllers, minimal APIs, filters, dependency injection, validation — and exposes it as a complete MCP toolset with zero duplication, zero wrappers, zero YAML, and zero ceremony. You keep writing normal .NET code. ZeroMcp turns it into AI‑ready tools automatically. Your API stays your API. ZeroMcp handles the MCP.

---

## A single attribute or method call
Add `[Mcp]` to a controller action or call `.AsMcp()` on a minimal API. ZeroMcp generates tool definitions, JSON schemas, parameter metadata, examples, categories, roles, policies, streaming support, and result enrichment from the code you already have. This mirrors the simplicity that made Newtonsoft.Json the default: your code becomes more powerful without changing how you write it.

---

## A UI that makes your tools visible
ZeroMcp includes a built‑in Tool Inspector UI that lets you browse tools, inspect schemas and metadata, execute tools live, validate examples, debug behaviour, and understand how your API appears to AI agents. Swagger had Swagger UI. ZeroMcp has the Tool Inspector. This transforms ZeroMcp from an adapter into a platform.

---

## Built for real systems
ZeroMcp integrates with the full ASP.NET Core pipeline: dependency injection, model binding, validation, filters, authentication, authorization, logging, OpenTelemetry, correlation IDs, governance, and policy‑based tool visibility. It works with brownfield and greenfield systems, microservices, and monoliths. This foundation makes ZeroMcp viable for production and enterprise environments.

---

## A roadmap designed for long‑term adoption
ZeroMcp evolves in phases:

- Phase 1: Core MCP adapter  
- Phase 2: Metadata enrichment and examples  
- Phase 3: Tool Inspector UI (we are here!)  
- Phase 4: Auditing, versioning, governance, enterprise controls  
- Phase 5: Multi‑language ecosystem (Node, Go, Rust, Python)  
- Phase 6: Full developer console and AI‑native workflows  

Each phase will further reduce friction.

---

## The goal: become the default
ZeroMcp aims to be the default MCP framework for .NET — the tool developers reach for without thinking. It is designed to feel natural, low‑friction, and unobtrusive, so that replacing it would add complexity rather than remove it. 

It exists because the duplication problem it solves only becomes visible when you are asked to make a large, established system “AI‑enabled” — not when working from small greenfield examples.

In that moment, it becomes clear that there should have been a library to expose existing APIs to AI agents without rewriting them — and since there wasn’t one, ZeroMcp was created to fill that gap.


---

