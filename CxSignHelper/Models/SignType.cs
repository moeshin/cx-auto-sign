namespace CxSignHelper.Models
{
    public enum SignType
    {
        // 普通签到
        Normal,

        // 图片签到
        Photo,

        // 二维码签到
        Qr,

        // 手势签到
        Gesture,

        // 位置签到
        Location,

        // 签到码签到
        Code,

        // 类型总数
        Length,

        // 未知签到类型
        Unknown = -1
    }
}