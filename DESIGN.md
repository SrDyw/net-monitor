---
name: Net Monitor
description: Connection traffic read as a tide on a nautical chart, in a day plate and a night plate.
colors:
  deep-water: "#0B1F33"
  night-abyss: "#08192A"
  night-ink: "#E6F0F6"
  night-muted: "#8FAABF"
  night-rule: "#1C3851"
  night-rule-strong: "#3A6489"
  night-datum: "#F2F7FA"
  day-ground: "#F5F9FB"
  day-ground-2: "#E8F0F5"
  day-muted: "#4C677D"
  day-rule: "#D5E2EA"
  day-rule-strong: "#9DB7CA"
  shoal: "#A8CFE0"
  mid-depth: "#4F8DB5"
  night-deep: "#2F5F86"
  day-deep: "#1F4E79"
  night-ebb-shallow: "#9CCBB5"
  day-ebb-shallow: "#A9D4C0"
  ebb-mid: "#4E8F7C"
  night-ebb-deep: "#2C5B55"
  day-ebb-deep: "#1F5A4F"
  land-buff: "#E3D6A8"
  chart-magenta: "#C2187A"
  night-magenta: "#EC6AAE"
  day-ok: "#1F8A5B"
  night-ok: "#7FD1AE"
  watermark-plate: "#0B1F33D9"
  watermark-muted: "#C9DAE6"
  watermark-alert: "#FF9CCF"
typography:
  headline:
    fontFamily: "Bahnschrift"
    fontSize: "15px"
    fontWeight: 700
    fontFeature: "tnum"
  body:
    fontFamily: "Bahnschrift"
    fontSize: "14px"
    fontWeight: 400
    fontFeature: "tnum"
  body-strong:
    fontFamily: "Bahnschrift"
    fontSize: "14px"
    fontWeight: 700
    fontFeature: "tnum"
  label:
    fontFamily: "Bahnschrift"
    fontSize: "13px"
    fontWeight: 400
    fontFeature: "tnum"
  footnote:
    fontFamily: "Bahnschrift"
    fontSize: "12px"
    fontWeight: 400
    fontFeature: "tnum"
  annotation:
    fontFamily: "Bahnschrift"
    fontSize: "12px"
    fontWeight: 600
    fontFeature: "tnum"
  chart-label:
    fontFamily: "Bahnschrift"
    fontSize: "11px"
    fontWeight: 400
    fontFeature: "tnum"
  chart-label-strong:
    fontFamily: "Bahnschrift"
    fontSize: "11px"
    fontWeight: 600
    fontFeature: "tnum"
rounded:
  none: "0px"
spacing:
  trace-gap: "6px"
  row-tight: "8px"
  heading-gap: "10px"
  row: "12px"
  bottom: "16px"
  column: "20px"
  gutter: "24px"
  strip: "52px"
  almanac: "300px"
components:
  rule-button:
    backgroundColor: "transparent"
    textColor: "{colors.night-ink}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "6px 12px"
  rule-button-hover:
    backgroundColor: "{colors.night-rule}"
    textColor: "{colors.night-ink}"
  rule-button-pressed:
    backgroundColor: "{colors.night-ink}"
    textColor: "{colors.deep-water}"
  segment:
    backgroundColor: "transparent"
    textColor: "{colors.night-muted}"
    typography: "{typography.label}"
    rounded: "{rounded.none}"
    padding: "4px 10px"
  segment-hover:
    textColor: "{colors.night-ink}"
  segment-checked:
    backgroundColor: "{colors.shoal}"
    textColor: "{colors.deep-water}"
  status-lamp:
    backgroundColor: "{colors.night-ok}"
    rounded: "{rounded.none}"
    size: "10px"
  tooltip:
    backgroundColor: "{colors.night-abyss}"
    textColor: "{colors.night-ink}"
    rounded: "{rounded.none}"
  watermark-plate:
    backgroundColor: "{colors.watermark-plate}"
    textColor: "{colors.night-ink}"
    typography: "{typography.headline}"
    rounded: "{rounded.none}"
    padding: "8px 12px 6px 12px"
    width: "300px"
  tray-menu:
    backgroundColor: "{colors.night-abyss}"
    textColor: "{colors.night-ink}"
    rounded: "{rounded.none}"
  tray-menu-item-hover:
    backgroundColor: "{colors.night-rule}"
---

# Design System: Net Monitor

## Overview

**Creative North Star: "The Tide Table"**

Net Monitor is drawn as a nautical chart. Download is water that floods above a chart datum, upload ebbs below it, and both are cut into three flat depth bands at their contour marks. Outages are land: buff bands that stay on the chart and are never washed away. Everything else on screen is either a reading taken off that chart or a one-pixel rule that holds the reading in place.

The system comes in two plates that share every brush key. The night plate is deep-water navy with pale ink. The day plate is chart white with deep-water ink. The window follows the Windows theme. The click-through watermark always uses the night plate, with its own heavier plate and lifted muted and alert inks, so it stays legible over any window. Density is that of a tool built for one expert: a 52 DIP state strip, a 300 DIP almanac column, and nothing ornamental.

The chart has no shadows, gradients, rounded corners, or glyph icons. Direction marks, ticks, the lamp and the tray icon are drawn geometry. This system describes the shipped surfaces: the main window, the watermark overlay, the tray icon, and the tray menu. All sizes are WPF device-independent pixels (1px = 1 DIP); "device pixel" means a physical pixel after DPI scaling.

**Key Characteristics:**
- A mirrored tide chart around a datum, with three flat depth bands per side.
- Day and night plates behind shared brush keys, following the system theme.
- One face (Bahnschrift) with tabular figures everywhere, including drawn chart lettering.
- Every rule is exactly one device pixel, snapped. Radius is zero everywhere.
- Outages are buff land bands that stay on the chart for the whole window.
- Magenta means alarm, and nothing else is magenta.

## Colors

A cold depth family for download, a sea-green ebb family for upload, buff for land, and chart magenta for alarm, all on a ground that is either deep water or chart white.

### Primary
- **Shoal Blue** (shoal): the shallowest download band, the checked range segment, the download trace, the watermark's down mark and the tray icon's water. It is the system's most visible signal colour. Shared by both plates.
- **Mid-Depth Blue** (mid-depth): the middle download band. Shared by both plates.
- **Deep Channel** (night-deep / day-deep): the download band nearest the datum. The day plate uses a darker value so the band holds against white.

### Secondary
- **Shallow Ebb** (night-ebb-shallow / day-ebb-shallow): the outermost upload band, the upload trace and the watermark's up mark.
- **Mid Ebb** (ebb-mid): the middle upload band, and the ebb strip of the tray icon. Shared by both plates.
- **Deep Ebb** (night-ebb-deep / day-ebb-deep): the upload band nearest the datum.

### Tertiary
- **Land Buff** (land-buff): outage bands on the chart and the "no network" lamp. It is lettered in deep-water ink (the land-ink role). Shared by both plates.
- **Chart Magenta** (chart-magenta on the day plate, night-magenta on the night plate): the alert role. It colours the latency value and trace above 100 ms, the lamp when latency is high or the internet is down, and the tray tile's water while the link is down. The tray tile always uses night-magenta because the tile is always deep water.
- **Sounding Green** (day-ok / night-ok): the lamp when the link is connected. It is used nowhere else.

### Neutral
- **Deep Water** (deep-water): the night ground, the day ink, the ink used on land, the text on the checked segment, and the tray tile's square.
- **Abyss** (night-abyss / day-ground-2): the ground-2 role, used for tooltips and the tray menu ground.
- **Chart White** (day-ground): the day ground.
- **Night Ink** (night-ink): night text and pressed-button fill.
- **Muted Sounding** (night-muted / day-muted): labels, units, secondary state text, chart scale and axis text.
- **Rule** (night-rule / day-rule): hairlines, contour rules, trace baselines, button and menu hover fill.
- **Strong Rule** (night-rule-strong / day-rule-strong): control borders, heading underlines, the time axis and the menu border.
- **Datum** (night-datum, or deep-water on the day plate): the chart datum line, the brightest rule on the chart.
- **Watermark Overrides** (watermark-plate, watermark-muted, watermark-alert): used only by the overlay. In XAML ARGB notation, the plate is `#D90B1F33`, which is deep water at 85% opacity. The overlay's muted and alert inks are lighter so they hold over white and black windows.

### Named Rules
**The Shared Keys Rule.** Every surface asks for a role (GroundBrush, InkBrush, Down3Brush and the others) through a dynamic resource, never a hex value. Changing the plate changes the app.

**The Magenta Is Alarm Rule.** Magenta appears only for high latency, no internet, and the alerting tray tile. It is never decorative.

**The Land Stays Rule.** An outage is drawn in land buff for as long as it is inside the chart window, edged by a one-device-pixel coastline in land ink. It is never erased, faded or interpolated over.

## Typography

**Display Font:** none
**Body Font:** Bahnschrift (the system DIN-style face)
**Label/Mono Font:** Bahnschrift with tabular figures (`Typography.NumeralAlignment="Tabular"`), so readings do not jitter

**Character:** Bahnschrift reads like the engineering lettering on a chart. One face carries every role. Rank comes from weight, case and rules, not from scale.

### Hierarchy
- **Headline** (Bold, 15px): state title (CONECTADO / SIN INTERNET / SIN RED), section heads (MAREA DE TRÁFICO, AHORA, SESIÓN), and the watermark's readout values.
- **Body** (Regular, 14px): the window default. It sets almanac row labels in muted ink, uppercase.
- **Body Strong** (Bold, 14px): almanac values. The unit follows in muted ink at the same size.
- **Label** (Regular, 13px): state detail and ping note, command buttons and range segments. The checked segment is SemiBold.
- **Footnote** (Regular, 12px): the closing note at the foot of the almanac, and units in the watermark.
- **Annotation** (SemiBold, 12px): chart callouts (PLEAMAR, MÁX. SUBIDA, CORTE).
- **Chart Label** (Regular, 11px): contour scale and time axis, in muted ink.
- **Chart Label Strong** (SemiBold, 11px): the BAJADA / SUBIDA tags on the datum.

### Named Rules
**The Untracked Caps Rule.** Names of things (state title, section heads, almanac labels, chart callouts) are set in uppercase and left untracked, because WPF has no letter-spacing. Commands, tooltips, axis times and notes stay in sentence or lower case.

**The One Size Almanac Rule.** Inside the almanac, every row label and value is 14px. Values rank by Bold and labels by muted ink, never by a larger size.

## Layout

The main window opens at 1000 × 680 DIP, with a minimum of 760 × 520. It is a two-row grid:

- **State strip:** 52 DIP tall, with 24 DIP side padding and a one-pixel rule beneath. Lamp, state title, detail and note sit on the left, 24 DIP apart. The watermark command sits on the right.
- **Body:** the tide chart fills the left column, with 24 DIP margins (20 DIP top, 16 DIP bottom). The almanac takes a fixed 300 DIP right column, set off by a one-pixel left rule and padded 20 DIP (16 DIP at the bottom).

Spacing follows a few repeated steps:

- 6 DIP between a reading and its trace.
- 8 DIP between session rows.
- 10 DIP between a section head and its strong rule.
- 12 DIP between almanac readings.
- 14 DIP between the chart header and the chart.
- 24 DIP before a new almanac section.

Inside the chart, a 68 DIP right gutter holds the contour scale so labels never cover the newest water. A 26 DIP top lane and a 42 DIP bottom lane hold the callouts and the time axis. The datum sits at 62% of the plot height, so download gets the larger share.

The watermark is a fixed 300 DIP plate, padded 8 / 12 / 6 DIP. It holds one reading row (lamp, down, up, latency) above a 28 DIP compact tide chart that covers the last 120 s. It is always on top, click-through, and never takes focus. Layout rounding and device-pixel snapping are on for every window.

## Elevation & Depth

The system is completely flat. It uses no shadows, no gradients and no blur. Depth is literal: the tide's depth bands are flat fills, clipped one per contour band, getting paler as the water gets higher. Layering is tonal only: a tooltip or menu sits on the ground-2 tone with a one-pixel strong rule around it, and the watermark is a translucent deep-water plate. The tray menu renderer is set up so every native gradient stop is the same flat colour.

### Named Rules
**The Flat Chart Rule.** No surface casts a shadow and no fill ramps. When something needs to stand apart, it gets a one-pixel rule or a ground-2 tone.

## Shapes

All corners are square (0px radius), including the status lamp (a 10 DIP square, or 8 DIP in the watermark). The menu renderer has rounded edges turned off. Borders are always one pixel, and chart rules are snapped to the centre of a device pixel by the shared chart-ink helper, so they land on exactly one pixel row at any DPI.

Data lines are the one deliberate exception. Traces are drawn 1.5 device pixels wide, and the watermark's direction marks use a 1.5 DIP stroke, so a reading line reads as heavier than a rule. Thresholds use a dashed hairline (2 on, 3 off), as with the 100 ms latency limit. Direction marks are drawn as a stem and a chevron (10 × 10 geometry), and the menu tick is a drawn three-point stroke. None of these are glyph characters.

### Named Rules
**The Device Pixel Rule.** Every rule, border, contour, datum and coastline is exactly one device pixel and snapped. Only data traces and drawn marks may be heavier.

## Components

### Buttons
Each command is a ruled frame, the button form a chart's legend box would take.
- **Shape:** square (0px), one-pixel strong-rule border.
- **Primary (Rule Button):** transparent fill, ink text, 13px label, 12 × 6 DIP padding. A secondary run inside it (the hotkey) is set in muted ink.
- **Hover / Pressed / Focus:** hover fills with the rule tone. Pressed fills with ink and turns the text to ground. Disabled drops to 45% opacity. Keyboard focus draws a one-pixel ink rectangle 3 DIP outside the control, with no radius (the shared chart focus style).

### Range Segment
The range picker (1 / 5 / 15 min) is a single ruled strip of radio buttons. Neighbouring borders overlap by 1 DIP, so the strip shows one line between segments rather than two.
- **Style:** transparent, strong-rule border, muted 13px text, 10 × 4 DIP padding.
- **State:** hover lifts the text to ink. Checked fills with shoal blue, sets the border to shoal, and sets the text in deep water at SemiBold. Ctrl+1/2/3 select the ranges, and the strip is a single tab stop.

### Status Lamp
A flat square whose colour follows the link state: sounding green when connected, magenta when latency is high or the internet is down, land buff when there is no network, and muted otherwise.

### Almanac Row
A reading with its proof underneath. The muted uppercase label sits on the left and the bold value with a muted unit on the right, all at 14px. Under it, 6 DIP down, is a 20 DIP trace of the last 60 s. The trace draws a one-pixel baseline across the full minute, so a young trace reads as the start of a line. The latency trace adds the dashed 100 ms limit and switches to magenta for any segment above it. Section heads are 15px Bold, followed by a one-pixel strong rule.

### Tide Chart (signature)
This is the mirrored chart that carries the thesis:
- **Download:** floods above the datum in three depth bands (deep, mid-depth, shoal, from the datum outward).
- **Upload:** ebbs below the datum in the three ebb bands.
- **Contours:** contour rules sit under the water, so they show only over dry chart. Contour steps round to 1, 2, 2.5 or 5 × 10ⁿ.
- **Datum:** the brightest one-pixel line on the chart.
- **Callouts:** the high-water mark (PLEAMAR) and the upload peak each get a one-pixel ink leader and a bold callout.
- **Outages:** buff land bands with a coastline on each side and a CORTE label with its duration.
- **Time axis:** a strong rule with 4 DIP ticks, reading back from "ahora".
- **Compact mode** (the watermark): drops every label, contour and coastline, and keeps water, land and datum.

### Chart Tag
Any text drawn over water or land sits on a patch of ground colour, 3 DIP wider on each side, so lettering stays legible over any band.

### Watermark Plate
A 300 DIP night-plate overlay at 85% deep water. It shows down and up readings, each led by a drawn mark in shoal or shallow ebb, and latency on the right, which turns magenta when high. When offline, the readings give way to the state title in watermark magenta, and the compact chart keeps showing the land.

### Tray Icon and Menu
- **Icon:** a 32 px tile. A deep-water square holds a tide silhouette in shoal (night magenta while the link is down) above a mid-ebb strip, cut by a 2 px datum in night-datum white.
- **Menu:** follows the active plate. The ground is ground-2, the border and separators are the strong rule, hover is the rule tone, the text is ink, the check mark is a drawn ink tick, and corners are square.

### Settings Controls
- **Switch:** a 40×20 DIP square track with a 1px strong rule and a 12 DIP block. On, the track fills with shoal and the block slides right in deep water.
- **Slider:** the filled run is a 3 DIP shoal bar, the rest a 1px strong rule, and the thumb a 12×16 ink block.
- **Text field:** ground-2 fill with a 1px strong rule, which turns ink while editing.
- **Scroll bar:** a 4 DIP strong-rule block, with no arrows.

### Plate List (process inspector)
- **Rows:** separated by a 1px rule, with hover in the rule tone. A selected row fills with shoal, like a checked segment. Text on a selected row switches to deep water (#0B1F33, with #1F4E79 for the muted line) so it keeps contrast on shoal.
- **Direction marks:** rows and summaries lead each rate with the drawn mark in mid-depth or mid-ebb (Down2 / Up2), which reads on both plates, and in deep water on a selected row.
- **List rates have no trace, deliberately:** they rank the rows, and only the selected group's rates are readings (its tide chart is their trace).

### Inspector Log
- **Face:** the one place monospace is allowed: Cascadia Mono 12 (LogFace), because columns of bytes and addresses must align. Tables elsewhere stay in Bahnschrift.
- **Kinds:** ranked by ink and weight, never by colour. DATOS and CONECTA are bold ink, DNS is regular ink, and CIERRA and INFO are muted. Blue and green keep meaning download and upload.
- **Arrows:** log lines may use the ↓ ↑ text characters because the log is copied and saved as plain text. This is the one exception to drawing arrows.
- **Overflow:** lines wrap rather than trim, so a destination or the "only from now on" note is never cut.

## Do's and Don'ts

### Do:
- **Do** request colour by role key (GroundBrush, InkBrush, RuleBrush, Down1–3, Up1–3, LandBrush, AlertBrush, OkBrush, DatumBrush) through a dynamic resource, so both plates and the theme switch keep working.
- **Do** snap every rule to one device pixel through the chart-ink helper, and keep borders at 1px with 0px radius.
- **Do** set every number in Bahnschrift with tabular figures, and put the unit after it in muted ink.
- **Do** give every live number its trace or its place on the chart.
- **Do** put chart lettering on a ground-colour tag whenever it crosses water or land.
- **Do** draw marks and icons as geometry (paths, rectangles, drawn ticks).
- **Do** keep the watermark on the night plate with its own plate, muted and alert overrides, whatever the system theme.

### Don't:
- **Don't** add shadows, gradients, blur or rounded corners to any surface, menu or control.
- **Don't** use magenta for anything except high latency, loss of internet, and the alerting tray tile.
- **Don't** erase, fade or smooth over an outage; it stays as land until it leaves the window.
- **Don't** rank almanac readings by size; use weight, case and rules.
- **Don't** use font glyphs or emoji for arrows, checks or status; draw them. The plain-text inspector log is the only exception.
- **Don't** use the connected-link green or the magenta for anything else, such as the inspector's running state or a process exiting; those read in muted ink.
- **Don't** hardcode hex colours in new XAML; use a plate key.
