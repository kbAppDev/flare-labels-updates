# Flare Tools visual foundation

Labels 1.1.0 establishes the first shared Flare Tools shell. This release updates Labels; other applications can adopt this same foundation in subsequent releases.

## Brand and color

| Role | Value | Use |
| --- | --- | --- |
| Flare black | #181A18 | Window header, navigation and family identity |
| Flare red | #D72832 | Brand accents and address issues |
| Flare lime | #99CC00 | Primary actions, selection and progress |
| Text | #20241E | Main text on light surfaces |
| Secondary text | #5D6459 | Supporting text on light surfaces |
| Light surface | #F2F4EE | Workspace; gentle lime tint |
| White glass | #FCFFFFFF to #EBFAFBF7 | Cards with a subtle highlight |

Use dark text on lime. Use white or pale neutral text on black. Never use lime as small text on white. Errors always include words; color alone must not carry meaning.

## Shared shell

The family name is **Flare Tools**. The task appears beside it, for example **Flare Tools / Labels**. Preserve executable, installer, preferences and update identities during visual migrations.

Use Segoe UI, 13-DIP interface text, 25–29-DIP main headings, 24-pixel card corners, 18-pixel button corners and 14-pixel field corners. Keep a visible keyboard focus ring. Reuse the same spacing, pill actions, soft shadows and consistent field labels. Keep text opaque and readable; glass is a surface cue.

Windows 11 uses native window rounding. Older Windows keeps the same opaque surfaces and controls. Avoid layered transparent windows, continuous animation and expensive background effects.

## Interaction

Show the task sequence and an obvious next action. Import and export have consistent locations and keyboard shortcuts. Explain issue counts in words and provide a direct review action. Use action names on confirmation buttons. Keep dialog actions visible while content scrolls.

The icon family uses a simple red flame, a clear tool symbol, restrained lime accents and a charcoal rounded tile. No lettering or intricate details inside small icons. Package multiple icon sizes and use high quality image scaling.

## Reuse

The reusable WPF tokens and controls live in `Themes/FlareTools.xaml`. The native corner helper is `WindowEffects.cs`. Use these as the starting point for the next Flare Tools desktop application, with matching family typography, icon language and update controls.
