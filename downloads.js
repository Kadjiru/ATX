const downloadCount = document.getElementById("download-count");
const arxPackage = /^ARX-(?:(?:Beta-)?Setup-\d+\.\d+\.\d+(?:-beta)?\.exe|\d+\.\d+\.\d+(?:-beta)?-win-x64\.zip)$/i;

async function loadReleaseDownloads() {
  let total = 0;
  const newest = {};
  let page = 1;
  let hasNextPage;
  const signal = AbortSignal.timeout(7000);

  do {
    const response = await fetch(
      `https://api.github.com/repos/Kadjiru/ATX/releases?per_page=100&page=${page}`,
      { signal },
    );
    if (!response.ok) throw new Error("GitHub releases are unavailable");

    for (const release of await response.json()) {
      if (release.draft || !release.published_at) continue;
      const packages = release.assets.filter(asset => arxPackage.test(asset.name));
      for (const asset of packages) total += asset.download_count;

      if (!packages.length) continue;
      const channel = release.prerelease ? "beta" : "stable";
      for (const key of ["latest", channel]) {
        if (!newest[key] || release.published_at > newest[key].release.published_at) {
          newest[key] = { release, packages };
        }
      }
    }

    hasNextPage = response.headers.get("Link")?.includes('rel="next"');
    page++;
  } while (hasNextPage);

  return { total, newest };
}

const downloadLinks = document.querySelectorAll("[data-download]");
let linksReady = false;

const prepareDownloads = loadReleaseDownloads().then(({ total, newest }) => {
  downloadCount.textContent = total.toLocaleString(document.documentElement.lang);
  for (const [channel, { release, packages }] of Object.entries(newest)) {
    const installer = packages.find(asset => /\.exe$/i.test(asset.name));
    const portable = packages.find(asset => /\.zip$/i.test(asset.name));
    const destinations = { installer, portable, preferred: installer ?? portable };
    const releaseName = release.name || release.tag_name;

    for (const link of downloadLinks) {
      if (link.dataset.channel === channel) {
        link.href = destinations[link.dataset.download]?.browser_download_url ?? release.html_url;
      }
    }
    document.querySelector(`[data-release-version="${channel}"]`).textContent =
      channel === "latest" ? releaseName.toUpperCase() : releaseName;
  }
}).catch(() => {
  downloadCount.textContent = downloadCount.dataset.unavailable;
}).finally(() => {
  linksReady = true;
});

for (const link of downloadLinks) {
  link.addEventListener("click", async event => {
    if (linksReady || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    await prepareDownloads;
    window.location.assign(link.href);
  });
}
