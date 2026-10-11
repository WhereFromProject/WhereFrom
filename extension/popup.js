const labels = {
  saved: "Last download recorded.",
  ignored: "Last download was already recorded or had no usable URL.",
  native_host_unavailable: "Native Host unavailable. Check registration and extension ID.",
  origin_not_allowed: "Extension ID is not registered for this browser.",
  file_not_found: "Downloaded file could not be found on disk.",
  storage_unavailable: "Local provenance database is unavailable.",
  download_lookup_failed: "Could not read the completed download.",
  invalid_message: "Native Host rejected the download message."
};
chrome.storage.local.get("lastStatus").then(({ lastStatus }) => {
  document.getElementById("status").textContent = lastStatus
    ? (labels[lastStatus.code] ?? `Capture error: ${lastStatus.code}`)
    : "Waiting for a new completed download.";
  if (lastStatus?.at) document.getElementById("time").textContent = new Date(lastStatus.at).toLocaleString();
});
