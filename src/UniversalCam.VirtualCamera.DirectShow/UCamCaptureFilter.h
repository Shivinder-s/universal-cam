#pragma once

// {12345678-1234-1234-1234-123456789012}
DEFINE_GUID(CLSID_UCamCaptureFilter,
            0x12345678, 0x1234, 0x1234, 0x12, 0x34, 0x12, 0x34, 0x56, 0x78, 0x90, 0x12);

class UCamCaptureFilter : public CSource
{
public:
    DECLARE_IUNKNOWN;

    static CUnknown *WINAPI CreateInstance(IUnknown *punk, HRESULT *phr);

    UCamCapturePin *m_paStreams[1];

private:
    UCamCaptureFilter(IUnknown *punk, HRESULT *phr);
    ~UCamCaptureFilter();
};
