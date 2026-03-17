#include "pch.h"
#include "UCamCapturePin.h"

const AMOVIESETUP_MEDIATYPE sudOpPinTypes =
    {
        &MEDIATYPE_Video,  // GUID of the media type
        &MEDIASUBTYPE_NV12 // GUID of the media subtype
};

UCamCapturePin::UCamCapturePin(HRESULT *phr, CSource *pFilter)
    : CSourceStream(NAME("UCamCapturePin"), phr, pFilter, L"Capture"),
      m_rtLastSampleTime(0),
      m_dwFrameCount(0)
{
    // Initialize video info header
    ZeroMemory(&m_videoInfo, sizeof(m_videoInfo));

    VIDEOINFOHEADER &vih = m_videoInfo;
    vih.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    vih.bmiHeader.biWidth = DEFAULT_WIDTH;
    vih.bmiHeader.biHeight = DEFAULT_HEIGHT;
    vih.bmiHeader.biBitCount = DEFAULT_BITCOUNT;
    vih.bmiHeader.biCompression = mmioFOURCC('N', 'V', '1', '2');
    vih.bmiHeader.biSizeImage = DEFAULT_WIDTH * DEFAULT_HEIGHT * 3 / 2;
    vih.bmiHeader.biPlanes = 1;
    vih.bmiHeader.biXPelsPerMeter = 0;
    vih.bmiHeader.biYPelsPerMeter = 0;
    vih.bmiHeader.biClrUsed = 0;
    vih.bmiHeader.biClrImportant = 0;

    // Set frame rate: 30fps
    vih.AvgTimePerFrame = (LONGLONG)(10000000.0 / DEFAULT_FPS);

    if (FAILED(*phr))
        return;
}

UCamCapturePin::~UCamCapturePin()
{
}

HRESULT UCamCapturePin::CheckMediaType(const CMediaType *pMediaType)
{
    if (pMediaType == NULL)
        return E_POINTER;

    if (*pMediaType->Type() != MEDIATYPE_Video)
        return E_INVALIDARG;

    if (*pMediaType->Subtype() != MEDIASUBTYPE_NV12)
        return E_INVALIDARG;

    if (*pMediaType->FormatType() != FORMAT_VideoInfo)
        return E_INVALIDARG;

    return S_OK;
}

HRESULT UCamCapturePin::GetMediaType(int iPosition, CMediaType *pmt)
{
    if (iPosition < 0)
        return E_INVALIDARG;
    if (iPosition > 0)
        return VFW_S_NO_MORE_ITEMS;

    if (pmt == NULL)
        return E_POINTER;

    pmt->SetType(&MEDIATYPE_Video);
    pmt->SetSubtype(&MEDIASUBTYPE_NV12);
    pmt->SetFormatType(&FORMAT_VideoInfo);
    pmt->SetTemporalCompression(FALSE);

    VIDEOINFOHEADER *pVih = (VIDEOINFOHEADER *)pmt->AllocFormatBuffer(sizeof(VIDEOINFOHEADER));
    if (pVih == NULL)
        return E_OUTOFMEMORY;

    CopyMemory(pVih, &m_videoInfo, sizeof(VIDEOINFOHEADER));
    pmt->SetSampleSize(m_videoInfo.bmiHeader.biSizeImage);

    return S_OK;
}

HRESULT UCamCapturePin::DecideBufferSize(IMemAllocator *pAlloc, ALLOCATOR_PROPERTIES *pProperties)
{
    ASSERT(pAlloc);
    ASSERT(pProperties);

    pProperties->cBuffers = 1;
    pProperties->cbBuffer = m_videoInfo.bmiHeader.biSizeImage;

    ALLOCATOR_PROPERTIES Actual;
    HRESULT hr = pAlloc->SetProperties(pProperties, &Actual);
    if (FAILED(hr))
        return hr;

    if (Actual.cbBuffer < pProperties->cbBuffer)
        return E_FAIL;

    return S_OK;
}

HRESULT UCamCapturePin::SetMediaType(const CMediaType *pmt)
{
    HRESULT hr = CSourceStream::SetMediaType(pmt);
    if (FAILED(hr))
        return hr;

    VIDEOINFOHEADER *pVih = (VIDEOINFOHEADER *)pmt->Format();
    if (pVih == NULL)
        return E_POINTER;

    CopyMemory(&m_videoInfo, pVih, sizeof(VIDEOINFOHEADER));
    return S_OK;
}

HRESULT UCamCapturePin::FillBuffer(IMediaSample *pms)
{
    ASSERT(pms);

    // TODO: Read frame from SharedMemoryBridge (UniversalCam_VCam_Frame MMF)
    // For now, fill with black (placeholder)

    BYTE *pData = NULL;
    long cbData = pms->GetSize();

    HRESULT hr = pms->GetPointer(&pData);
    if (FAILED(hr))
        return hr;

    // Fill with black (Y=0, U=128, V=128)
    ZeroMemory(pData, cbData);

    // UV plane (starts at Y plane size)
    int ySize = m_videoInfo.bmiHeader.biWidth * m_videoInfo.bmiHeader.biHeight;
    FillMemory(pData + ySize, cbData - ySize, 0x80); // U and V = 128

    pms->SetActualDataLength(cbData);

    // Set timestamp
    LONGLONG rtStart = m_rtLastSampleTime;
    LONGLONG rtStop = rtStart + m_videoInfo.AvgTimePerFrame;

    pms->SetTime(&rtStart, &rtStop);
    m_rtLastSampleTime = rtStop;

    // Increment frame count
    m_dwFrameCount++;

    // Mark keyframe
    pms->SetSyncPoint(TRUE);

    return S_OK;
}
