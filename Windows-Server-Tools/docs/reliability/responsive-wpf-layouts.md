# Responsive WPF layouts

## Main window

The primary window uses one vertically scrolling content column with a 720-pixel maximum content width and no page-level horizontal scrolling. Mutation actions use a 44-pixel minimum height and wrap within their groups. The reviewed initial-setup and recovery cards cap their height, keep actions visible, and scroll their own long details.

## Common server-features window

The commonly installed server-features window no longer uses a fixed 520-pixel canvas or 29-pixel absolutely positioned buttons. It now provides:

- a vertical content column that reflows from the supported 340-pixel window minimum;
- disabled page-level horizontal scrolling;
- a named visual heading and explanatory text; .NET Framework 4.7.2 WPF does not expose the newer automation heading-level property;
- wrapped feature actions with 44-pixel minimum targets;
- continuous keyboard navigation and deterministic initial focus on the first available feature action;
- a bounded, internally scrolling recovery surface whose navigation, retry, dismiss, and persistent review controls also meet the 44-pixel target minimum;
- the currently selected application logo with a useful accessible name.

The feature actions retain their exact accessible names and help text, including the active-operation reason while another machine mutation owns the shared lease. Status changes target a real text automation peer and raise a live-region event.

## Remaining boundaries

Both windows still use the operating system title bar rather than custom frameless Material chrome. Localization, real high-scale interaction, and capture matrices remain separate release blockers. The source-level layout checks prove the declared responsive structure but do not replace packaged interaction at 100, 125, 150, and 200 percent display scale.

## Verification

`Windows-Server-Tools.Tests` asserts both windows have bounded vertical scrolling, wrapped action rows, live status, focus restoration, and persistent recovery access. Dedicated secondary checks reject the old fixed content width, 29-pixel actions, and page-level horizontal scroll while requiring its named heading, continuous keyboard order, seven 44-pixel action targets, and first-focus path.

Suggested articles: [Error recovery](error-recovery.md), [Local application-logo customization](../branding/custom-application-logo.md), and [WPF completeness](../completeness/wpf-universal-feature-inventory.md).
