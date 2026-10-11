const HOST = "org.wherefrom.provenance";
const RETRY_DELAYS_MS = [0, 1000, 3000];

function browserName() {
  const brands = navigator.userAgentData?.brands ?? [];
  return brands.some(brand => /Microsoft Edge/i.test(brand.brand)) || /Edg\//.test(navigator.userAgent)
    ? "edge" : "chrome";
}

async function setStatus(code) {
  await chrome.storage.local.set({ lastStatus: { code, at: new Date().toISOString() } });
  await chrome.action.setBadgeText({ text: code === "saved" || code === "ignored" ? "" : "!" });
  if (code !== "saved" && code !== "ignored") {
    await chrome.action.setBadgeBackgroundColor({ color: "#b3261e" });
  }
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds));
}

async function captureCompleted(id) {
  for (const wait of RETRY_DELAYS_MS) {
    if (wait) await delay(wait);
    let items;
    try {
      items = await chrome.downloads.search({ id });
    } catch {
      await setStatus("download_lookup_failed");
      return;
    }
    const item = items[0];
    if (!item || item.state !== "complete") return;
    if (item.incognito) return;
    if (!item.filename || !item.startTime || !item.endTime) continue;

    const message = {
      schemaVersion: 1,
      event: "download.completed",
      download: {
        browser: browserName(),
        downloadId: String(item.id),
        path: item.filename,
        startedAt: item.startTime,
        completedAt: item.endTime,
        initialUrl: item.url ?? null,
        finalUrl: item.finalUrl ?? null,
        referrerUrl: item.referrer ?? null,
        incognito: false
      }
    };
    try {
      const response = await chrome.runtime.sendNativeMessage(HOST, message);
      const code = response?.Code ?? response?.code;
      if (code === "file_not_found") continue;
      await setStatus(response?.Ok || response?.ok ? code : (code || "native_host_error"));
      return;
    } catch {
      await setStatus("native_host_unavailable");
      return;
    }
  }
  await setStatus("file_not_found");
}

chrome.downloads.onChanged.addListener(delta => {
  if (delta.state?.current === "complete") {
    captureCompleted(delta.id);
  }
});
