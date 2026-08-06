namespace Client.MirScenes.Dialogs
{
    // ChatNoticeDialog 的 Client.Core seam（占位）：仅覆盖 ShowNotice 契约。
    // 真实 ChatNoticeDialog（公告横幅）移植时替换。
    public class ChatNoticeDialog
    {
        public void ShowNotice(string notice) { }
    }
}
