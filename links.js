// Add the Steam Community guide URL once published.
const steamGuideUrl = "";

if (steamGuideUrl) {
  const status = document.querySelector('[data-link="steam"]');
  const link = document.createElement("a");
  link.className = "button primary";
  link.href = steamGuideUrl;
  link.textContent = "Открыть Steam-гайд ↗";
  status.replaceWith(link);
}
