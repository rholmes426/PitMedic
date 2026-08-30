import path from "node:path";
import { createHash } from "node:crypto";
import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [
    cloudflareTest(async () => {
      const migrations = await readD1Migrations(
        path.join(import.meta.dirname, "../TelemetryWorker/migrations"),
      );
      return {
        wrangler: { configPath: "./wrangler.jsonc" },
        miniflare: {
          bindings: {
            TEST_MIGRATIONS: migrations,
            ADMIN_PASSCODE_HASH: createHash("sha256")
              .update("pitmedic-test-passcode")
              .digest("hex"),
            SESSION_SIGNING_KEY:
              "pitmedic-test-session-signing-key-with-more-than-32-characters",
          },
        },
      };
    }),
  ],
  test: {
    setupFiles: ["./test/apply-migrations.ts"],
  },
});
