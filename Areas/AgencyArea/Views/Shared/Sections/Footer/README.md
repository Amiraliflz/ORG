How to load the custom RTL pastel footer

This folder contains the custom footer partial `_Footer.cshtml` used by the Agency area. The footer HTML uses Tailwind utility classes and `lucide` icons. Below are recommended options to load and use it safely in your app.

1) Quick setup using CDN (development or prototype)

- Add these lines in the HEAD of your master layout (we added them to `_CommonMasterLayout.cshtml`):

```html
<script src="https://cdn.tailwindcss.com"></script>
<script src="https://unpkg.com/lucide@latest"></script>
```

- Ensure the agency front layout renders the footer partial. Example (we updated `_FrontLayout.cshtml`):

```razor
@await Html.PartialAsync("Sections/Footer/_Footer")
```

Notes: using the Tailwind CDN is fine for quick prototyping, but it's not recommended for production due to performance / control over the generated CSS.

2) Recommended production setup (build Tailwind)

- Install Tailwind as part of your frontend build (npm + Tailwind CLI or PostCSS integration).
- Compile a minimal Tailwind stylesheet that includes only the utilities used by the footer (or the whole site) and serve the compiled CSS e.g. `wwwroot/css/site-tailwind.css`.
- Remove the Tailwind CDN script from layouts and reference the compiled CSS instead.

3) If you prefer Bootstrap (no Tailwind)

- Convert the footer markup from Tailwind utilities to Bootstrap classes (or use the existing project styles). If you want, I can convert the current footer to match your Bootstrap-based theme.

4) Lucide icons

- The footer uses `lucide` and calls `lucide.createIcons()` in the footer script. If you include the lucide script in layout (we added it), icons will render automatically.
- Alternatively you can replace lucide usage with your project's icon set (FontAwesome, Tabler, etc.).

5) Accessibility & RTL

- The markup assumes RTL (`dir="rtl"`) pages. The project layouts in the Agency area appear to be RTL ready — verify `dir` attributes and CSS if anything looks left-aligned.

6) Security / performance

- If you include third-party CDN scripts, consider hosting them locally or using Subresource Integrity (SRI) for production.
- Avoid loading Tailwind via CDN in production; compile and ship a minimal CSS build.

If you want, I can:
- Convert the footer to Bootstrap markup to match the project styles.
- Add the Tailwind build steps and a sample `tailwind.config.js` + npm scripts to the repo.
- Replace lucide with the project's existing icon set.

Which option do you want me to implement next?
