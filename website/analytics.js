(() => {
  "use strict";

  const endpoint =
    "https://pitmedic-usage.pitmedic-usage-telemetry.workers.dev/v1/web-event";

  const normalizePath = (value) => {
    const path = value.toLowerCase().replace(/\/{2,}/g, "/");
    return path === "/" || path.endsWith("/") ? path : `${path}/`;
  };

  const path = normalizePath(window.location.pathname);
  let referrer = "";
  try {
    referrer = document.referrer ? new URL(document.referrer).hostname : "";
  } catch {
    referrer = "";
  }

  const send = (event, target = "") => {
    const body = JSON.stringify({
      protocol: 1,
      event,
      path,
      target,
      referrer,
    });
    const payload = new Blob([body], { type: "text/plain;charset=UTF-8" });
    if (navigator.sendBeacon?.(endpoint, payload)) return;
    fetch(endpoint, {
      method: "POST",
      body,
      headers: { "Content-Type": "text/plain;charset=UTF-8" },
      mode: "cors",
      credentials: "omit",
      keepalive: true,
    }).catch(() => undefined);
  };

  send("page_view");

  let engaged = false;
  const markEngaged = () => {
    if (engaged || document.visibilityState !== "visible") return;
    engaged = true;
    send("engaged");
    window.removeEventListener("scroll", checkScroll);
  };
  const checkScroll = () => {
    const available = document.documentElement.scrollHeight - window.innerHeight;
    if (available > 0 && window.scrollY / available >= 0.5) markEngaged();
  };
  window.addEventListener("scroll", checkScroll, { passive: true });
  window.setTimeout(markEngaged, 30_000);

  document.addEventListener(
    "click",
    (event) => {
      const link = event.target instanceof Element ? event.target.closest("a") : null;
      if (!link?.href) return;
      let destination;
      try {
        destination = new URL(link.href, window.location.href);
      } catch {
        return;
      }

      const release = destination.pathname.match(
        /^\/rholmes426\/PitMedic\/releases\/download\/(v\d+\.\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?)\/PitMedic-Setup-x64\.exe$/i,
      );
      if (destination.hostname === "github.com" && release) {
        send("download", `release:${release[1].toLowerCase()}`);
        return;
      }

      if (
        destination.hostname.replace(/^www\./, "") === "pitmedic.com" &&
        destination.pathname !== window.location.pathname
      ) {
        send("internal_navigation", normalizePath(destination.pathname));
      }
    },
    { capture: true },
  );
})();
