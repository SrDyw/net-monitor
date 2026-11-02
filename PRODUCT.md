# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows

Native Windows desktop app built with WPF. Not web, and not one of the mobile platforms; follow Windows desktop conventions (Fluent/Windows 11 window chrome, keyboard-first input, standard dialogs, system theme and DPI scaling).

## Stack

Delegated: .NET 9 + WPF + CommunityToolkit.Mvvm (MVVM with source generators). Chosen because it is the current .NET release, keeps WPF native without a heavy UI framework, and the toolkit removes MVVM boilerplate. No third-party control library has been chosen.

## Users

A single user: the owner (a developer), using it personally on their own Windows machine. No other audiences, accounts, or roles.

## Product Purpose

A personal Windows utility that shows the state of the owner's internet connection at a glance: live upload and download throughput, latency, connection state, recent history, and session totals.

Success: without switching windows, the owner can tell whether the connection is up, how fast traffic is moving, and whether latency is spiking.

Open decision: the product name. "Net Monitor" / `net-monitor` is a working name only.

## Capabilities and Constraints

- Runs locally on one Windows machine; no distribution, installer, licensing, or multi-user requirements have been stated.
- Metrics: upload and download rate (aggregated across active adapters), latency via periodic ping, connection state (connected / no internet / no network) plus the active adapter or Wi-Fi name, a rolling history graph of upload and download, and totals uploaded/downloaded since the app started.
- Two modes:
  - **Normal:** a regular window with the full readout and history graph.
  - **Watermark:** a compact, semi-transparent overlay, always on top and click-through (the mouse passes through it). Return to normal mode with a global hotkey or from the tray icon.
- Lives in the system tray: closing the window hides it to the tray; the tray menu switches modes and exits.

## Evidence on Hand

None. No logo, name, copy, or assets exist yet; do not fabricate any.

## Product Principles

1. Built for one expert user: favor speed, keyboard shortcuts, and density over onboarding and hand-holding.
2. Behave like a native Windows app: respect system theme, DPI, accessibility settings, and standard window behavior.
3. Glanceable first: the watermark must be readable over any content without stealing focus or clicks.
4. Keep scope to connectivity; no features beyond what the owner asked for.
