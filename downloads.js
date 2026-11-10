const downloadCount = document.getElementById("download-count");
const arxPackage = /^ARX-(?:Setup-\d+\.\d+\.\d+(?:-beta)?\.exe|\d+\.\d+\.\d+(?:-beta)?-win-x64\.zip)$/i;

async function updateDownloadCount() {
  try {
    let total = 0;
    let page = 1;
    let hasNextPage;
    const signal = AbortSignal.timeout(10000);

    do {
      const response = await fetch(
        `https://api.github.com/repos/Kadjiru/ATX/releases?per_page=100&page=${page}`,
        { signal },
      );
      if (!response.ok) throw new Error("GitHub releases are unavailable");

      const releases = await response.json();
      for (const { assets } of releases) {
        for (const asset of assets) {
          if (arxPackage.test(asset.name)) total += asset.download_count;
        }
      }

      hasNextPage = response.headers.get("Link")?.includes('rel="next"');
      page++;
    } while (hasNextPage);

    downloadCount.textContent = total.toLocaleString("ru-RU");
  } catch {
    downloadCount.textContent = "временно недоступны";
  }
}

updateDownloadCount();
