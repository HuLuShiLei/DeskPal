import type { Host } from "./host";

export const sharingMessage =
  "DeskPal 会把你的分类保存到服务器，在所有房间和电脑上通用，房间里的其他人不受影响。\n\n" +
  "同意后，还会用这些信息改进默认分类：\n" +
  "· 没有被识别的桌面应用：进程名、产品名、当天使用了多少分钟\n" +
  "· 你对桌面应用设置的分类\n\n" +
  "不会上传窗口标题、网页地址、媒体标题和使用的具体时间。多人一致的结果才会加入默认分类。可以随时在「设置」里关闭。";

/** 首次打开面板时询问一次。 */
export async function askSharing(host: Host) {
  const a = await host.ask({ title: "帮助改进默认分类", message: sharingMessage, okText: "同意分享", cancelText: "暂不分享" });
  await host.run({ type: "settings.shareUsage", enabled: a.ok }, a.ok ? "谢谢，可以随时在设置里关闭" : "不分享，可以随时在设置里开启");
}
