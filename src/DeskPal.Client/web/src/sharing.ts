import type { Host } from "./host";
import { t } from "./i18n";

/** 首次打开面板时询问一次。 */
export async function askSharing(host: Host) {
  const a = await host.ask({ title: t("sharing.title"), message: t("sharing.message"), okText: t("sharing.agree"), cancelText: t("sharing.decline") });
  await host.run({ type: "settings.shareUsage", enabled: a.ok }, a.ok ? t("sharing.thanks") : t("sharing.declined"));
}
