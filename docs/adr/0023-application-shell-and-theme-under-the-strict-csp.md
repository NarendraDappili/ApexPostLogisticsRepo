# ADR 0023: Application shell, navigation and theme under the strict CSP

Status: Accepted.

## Context

The original screens had a top navigation bar and a free-text or one-item branch dropdown on every form. Two problems followed:
- **The branch dropdown suggested a choice that did not exist.** Clerks and managers may only work at their home branch (the `SameBranchOnly` policy), so their dropdown listed one branch. Picking another branch by URL is refused with 403. Users read the single entry as missing data.
- **The navigation did not scale.** The top bar did not show which screen was current, crowded on small screens, and gave no sense of the screen groups (counter work, inventory, management, administration).

The UI round had three constraints:
- **The Content Security Policy must stay strict.** `script-src 'self'; style-src 'self'` with no `'unsafe-inline'` (docs/security.md). A test scans every view for inline script, `<style>`, `style=` and `on…=` handlers.
- **Behaviour must not change.** Routes, field names, model binding, authorization and the markup the integration tests assert must stay as they were.
- **No new package.** The brief limits packages, and ADR 0019 is the only exception.

## Options

1. **Keep the top bar and restyle it.** This is the least work, but it fixes neither problem.
2. **A UI component library or icon font from a CDN.** It needs the CDN's origin in `script-src`/`style-src`/`font-src` and a new dependency, so it fails two constraints.
3. **A sidebar shell built from Bootstrap 5.3 (already bundled), an inline SVG icon sprite and one stylesheet.** Role-aware navigation and a branch picker that follows the same access rule as the server.

## Decision

Option 3.

- **Layout.** A left sidebar holds the navigation, grouped into Overview, Counter, Inventory, Management and Administration. On phones it becomes Bootstrap's `offcanvas-lg` panel, opened from a menu button. A top bar shows a breadcrumb, the home-branch chip, the role and Sign out. A skip link leads to the main content.
- **Navigation follows authorization.** The layout shows each menu entry only to the roles its controller admits: Counter screens to clerks, Transactions to managers, Reports to managers, auditors and administrators, Administration to administrators. The server still enforces every rule; the menu only stops offering what would be refused. The current entry carries `aria-current="page"`.
- **The branch picker follows `BranchAccess.CanViewAllBranches`.** `ReferenceDataLookup.ViewableBranchesAsync` returns every branch for administrators and auditors, and only the home branch otherwise. `_BranchPicker.cshtml` shows a dropdown when there is a choice. With one branch, it shows the branch as text with a "Your home branch" badge, plus a hidden `branchCode` field. The posted field name and value are unchanged.
- **Readable enum labels.** `DisplayText.Of` shows "Counter Clerk", not "CounterClerk". `ReferenceDataLookup.EnumOptions` keeps the numeric option values that `GetEnumSelectList` produced, so model binding is unchanged.
- **Icons are an SVG sprite inside the page.** `_Icons.cshtml` defines `<symbol>`s, used with `<svg><use href="#i-…">`. Inline SVG markup is not script or style, so the CSP allows it. It uses presentation attributes only, never `style=`.
- **The theme is one stylesheet.** `wwwroot/css/site.css` sets the colours, about 50% yellow, 30% red and 20% black:
  - yellow is the main surface: the sidebar and the header bar;
  - red marks actions and the current place: primary buttons, the current menu entry, accent edges and links;
  - black carries text, icons and the footer.
  List tables use their own slate palette, so data rows stand apart from the brand chrome. Every text and background pair passes WCAG AA contrast.
- **The logo watermark is a same-origin file.** `wwwroot/img/logo-watermark.svg` is drawn by a fixed `body::after` layer behind the content, over a fixed `body::before` layer that holds the soft background glow. It is allowed by `img-src 'self'` and stays in view while long pages scroll.
  - Both layers are their own compositor layers (`will-change: transform`). Scrolling therefore moves the page over them without repainting the window.
  - The first version used `background-attachment: fixed`, which repainted the whole window on every scroll step and showed as flicker. A scroll trace in headless Edge measured 33 paints, 31 of them full-window, about 54 ms. After the change it measured 5 paints, about 6.5 ms.
  - The sticky top bar also dropped its backdrop blur for the same reason.

## Consequences

- **The CSP did not change,** and the view scan still passes. Headless Edge, as each role at desktop and phone widths, logged no console or CSP error.
- **No package was added.** The shell uses the Bootstrap, Popper and jQuery validation files that were already bundled.
- **Behaviour is unchanged.** All earlier tests pass without edits. `Branch_picker_and_menu_offer_only_what_the_signed_in_role_may_use` pins the new behaviour: the fixed branch for a clerk, the dropdown for an administrator, the role-filtered menu and a single current entry.
- **The menu duplicates the role list of each controller's `[Authorize]`.** A new screen needs both its `[Authorize]` attribute and its menu entry. If the menu were wrong, the server would still refuse the request; the user would only see an entry that does not work.
- **The low-stock threshold has one source.** The catalogue's low-stock flag uses the same `InventoryServiceOptions` singleton as stock alerts, the dashboard and the reports, so the screens cannot disagree.
