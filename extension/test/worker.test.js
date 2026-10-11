const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

function loadWorker(item, response = { Ok: true, Code: "saved" }, userAgent = "Chrome/130") {
  const calls = { sent: [], statuses: [], listeners: [] };
  const chrome = {
    downloads: {
      search: async () => [item],
      onChanged: { addListener: listener => calls.listeners.push(listener) }
    },
    runtime: { sendNativeMessage: async (_, message) => { calls.sent.push(message); return response; } },
    storage: { local: { set: async data => calls.statuses.push(data.lastStatus.code) } },
    action: { setBadgeText: async () => {}, setBadgeBackgroundColor: async () => {} }
  };
  const context = vm.createContext({ chrome, navigator: { userAgent },
    setTimeout: callback => callback(), Date, Promise });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "..", "worker.js"), "utf8"), context);
  return { calls, capture: id => vm.runInContext(`captureCompleted(${id})`, context) };
}

const complete = {
  id: 7, state: "complete", incognito: false, filename: "C:\\Downloads\\test.txt",
  startTime: "2026-10-03T10:00:00Z", endTime: "2026-10-03T10:00:03Z",
  url: "https://example.com/file?secret=1", finalUrl: "https://example.com/file?secret=1"
};

test("completed download sends one versioned event", async () => {
  const { calls, capture } = loadWorker(complete);
  await capture(7);
  assert.equal(calls.sent.length, 1);
  assert.equal(calls.sent[0].schemaVersion, 1);
  assert.equal(calls.sent[0].event, "download.completed");
  assert.equal(calls.sent[0].download.browser, "chrome");
  assert.deepEqual(calls.statuses, ["saved"]);
});

test("incomplete and incognito downloads are never sent", async () => {
  for (const item of [{ ...complete, state: "interrupted" }, { ...complete, incognito: true }]) {
    const { calls, capture } = loadWorker(item);
    await capture(7);
    assert.equal(calls.sent.length, 0);
  }
});

test("missing files get bounded retries", async () => {
  const { calls, capture } = loadWorker(complete, { Ok: false, Code: "file_not_found" });
  await capture(7);
  assert.equal(calls.sent.length, 3);
  assert.deepEqual(calls.statuses, ["file_not_found"]);
});

test("Edge events identify their browser", async () => {
  const { calls, capture } = loadWorker(complete, { Ok: true, Code: "saved" }, "Chrome/130 Edg/130");
  await capture(7);
  assert.equal(calls.sent[0].download.browser, "edge");
});

test("host rejection is visible without retaining a URL in extension storage", async () => {
  const { calls, capture } = loadWorker(complete, { Ok: false, Code: "origin_not_allowed" });
  await capture(7);
  assert.deepEqual(calls.statuses, ["origin_not_allowed"]);
  assert.equal(calls.sent.length, 1);
});
