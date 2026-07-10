using System;
using System.Threading.Tasks;

namespace RLC_LoadBank_SeparateVer.Services
{
    public interface ICLoadSequencer
    {
        // DEPRECATED: C부하 시퀀스가 PLC 내부로 이관됨. 인터페이스는 하위 호환성 유지용.
        Task<bool> RunAsync(int panelIndex, int stage, bool on, Action<string> log = null);
    }

    /// <summary>
    /// C부하 시퀀스는 PLC 내부 담당으로 변경됨.
    /// HMI는 C{n}_CMD 단일 신호만 전송하고, RESULT/알람 DI를 모니터링.
    /// 이 클래스는 기존 ServiceHub.CLoad 참조 유지를 위한 스텁.
    /// </summary>
    public class CLoadSequencer : ICLoadSequencer
    {
        public Task<bool> RunAsync(int panelIndex, int stage, bool on, Action<string> log = null)
            => Task.FromResult(true);
    }
}
