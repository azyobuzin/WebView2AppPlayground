document.getElementById("alert-form").onsubmit = (ev) => {
    ev.preventDefault()
    const inputEl = document.getElementById("alert-form-input")
    chrome.webview.postMessage({ type: "alert", content: inputEl.value });
    inputEl.value = ""
}

document.getElementById("btn-subwindow").onclick = (ev) => {
    ev.preventDefault()
    chrome.webview.postMessage({ type: "subwindow" });
}

if (location.pathname == "/index.html") {
    console.log("Start worker")
    new Worker("/worker.js")
}

const chan = new BroadcastChannel("worker-channel")
chan.onmessage = (ev) => {
    document.getElementById("txt-received").textContent = JSON.stringify(ev.data)
}
