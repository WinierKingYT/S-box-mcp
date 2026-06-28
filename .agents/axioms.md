# S&box MCP Immutable Core Axioms

This file contains the immutable core axioms for the S&box MCP coding agent. These rules are read-only and are validated automatically on every code compilation and prompt compilation.

## 1. Safety and Sandboxing
- **No Unsafe Code**: The use of C# `unsafe` blocks, unsafe structs, or pointers is strictly prohibited to prevent Access Violations (0xC0000005) in the C++ engine layer.
- **No Native Interop**: Do not use `DllImport`, `LibraryImport`, or other marshalling attributes that attempt to bypass the C# virtual machine.
- **No Unsafe Namespaces**: Do not import or invoke types from `System.Runtime.InteropServices` or `System.Reflection` unless explicitly marked as safe and approved.

## 2. S&box Component Architecture
- **Component-Centric Design**: All custom logic must inherit from `Sandbox.Component` or fit into the s&box Scene/GameObject architecture.
- **No Direct Thread Hijacking**: Avoid background thread manipulations that modify scene objects without dispatching to the main game loop thread.
- **Dynamic Resolving**: Use `TypeLibrary` to inspect and instantiate types from other assemblies dynamically to avoid hard assembly reference locks.
