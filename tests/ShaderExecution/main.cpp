#include <cstdio>
#include <cstring>
#include <d3d12.h>
#include <d3dcompiler.h>
#include <fstream>
#include <stdexcept>
#include <vector>
#include <windows.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
static void ok(HRESULT h) {
  if (FAILED(h)) {
    char s[80];
    snprintf(s, sizeof s, "HRESULT %08lx", (unsigned long)h);
    throw std::runtime_error(s);
  }
}
static void write(const char *path, const void *data, size_t size) {
  FILE *f = fopen(path, "wbx");
  if (!f)
    throw std::runtime_error("Fresh output required");
  if (fwrite(data, 1, size, f) != size)
    throw std::runtime_error("Write failed");
  fclose(f);
}
int main(int argc, char **argv) try {
  if (argc == 5 && !strcmp(argv[1], "--compile")) {
    std::ifstream file(argv[3], std::ios::binary);
    if (!file)
      throw std::runtime_error("Authored shader source missing");
    std::string source((std::istreambuf_iterator<char>(file)), {});
    ComPtr<ID3DBlob> program, errors;
    auto result = D3DCompile(source.data(), source.size(), "authored", nullptr,
                             nullptr, "main", argv[2],
                             D3DCOMPILE_IEEE_STRICTNESS, 0, &program, &errors);
    if (errors)
      fwrite(errors->GetBufferPointer(), 1, errors->GetBufferSize(), stderr);
    ok(result);
    write(argv[4], program->GetBufferPointer(), program->GetBufferSize());
    puts("Authored DXBC compiled");
    return 0;
  }
  bool tangents = argc == 6 && !strcmp(argv[5], "--tangents");
  if (argc != 5 && !tangents)
    throw std::runtime_error("pixel output mode vertex [--tangents] required");
  std::ifstream f(argv[1], std::ios::binary);
  if (!f)
    throw std::runtime_error("Shader input missing");
  std::vector<char> shader((std::istreambuf_iterator<char>(f)), {});
  ComPtr<ID3D12Device> device;
  ok(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device)));
  D3D12_ROOT_PARAMETER params[1]{};
  params[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
  params[0].Constants = {0, 0, 4};
  D3D12_ROOT_SIGNATURE_DESC rootDesc{1, params, 0, nullptr,
                                     D3D12_ROOT_SIGNATURE_FLAG_NONE};
  if (tangents)
    rootDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
  ComPtr<ID3DBlob> rootBytes, error;
  ok(D3D12SerializeRootSignature(&rootDesc, D3D_ROOT_SIGNATURE_VERSION_1,
                                 &rootBytes, &error));
  ComPtr<ID3D12RootSignature> root;
  ok(device->CreateRootSignature(0, rootBytes->GetBufferPointer(),
                                 rootBytes->GetBufferSize(),
                                 IID_PPV_ARGS(&root)));
  std::ifstream vf(argv[4], std::ios::binary);
  if (!vf)
    throw std::runtime_error("Vertex shader missing");
  std::vector<char> vs((std::istreambuf_iterator<char>(vf)), {});
  D3D12_GRAPHICS_PIPELINE_STATE_DESC pso{};
  pso.pRootSignature = root.Get();
  pso.PS = {shader.data(), shader.size()};
  pso.VS = {vs.data(), vs.size()};
  pso.NumRenderTargets = 1;
  pso.RTVFormats[0] = DXGI_FORMAT_R32G32B32A32_FLOAT;
  pso.SampleDesc.Count = 1;
  pso.SampleMask = UINT_MAX;
  pso.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
  pso.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
  pso.RasterizerState.DepthClipEnable = TRUE;
  pso.BlendState.RenderTarget[0].RenderTargetWriteMask =
      D3D12_COLOR_WRITE_ENABLE_ALL;
  pso.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
  D3D12_INPUT_ELEMENT_DESC elements[] = {
      {"TANGENT", 0, DXGI_FORMAT_R32G32B32A32_UINT, 0, 0,
       D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0},
      {"TANGENT", 2, DXGI_FORMAT_R32G32B32A32_UINT, 0, 16,
       D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0}};
  if (tangents)
    pso.InputLayout = {elements, 2};
  ComPtr<ID3D12PipelineState> pipeline;
  ok(device->CreateGraphicsPipelineState(&pso, IID_PPV_ARGS(&pipeline)));
  const size_t bytes = 64 * 4 * sizeof(float);
  auto buffer = [&](D3D12_HEAP_TYPE type, D3D12_RESOURCE_STATES state,
                    D3D12_RESOURCE_FLAGS flags) {
    ComPtr<ID3D12Resource> r;
    D3D12_HEAP_PROPERTIES h{};
    h.Type = type;
    D3D12_RESOURCE_DESC d{};
    d.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    d.Width = bytes;
    d.Height = 1;
    d.DepthOrArraySize = 1;
    d.MipLevels = 1;
    d.SampleDesc.Count = 1;
    d.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    d.Flags = flags;
    ok(device->CreateCommittedResource(&h, D3D12_HEAP_FLAG_NONE, &d, state,
                                       nullptr, IID_PPV_ARGS(&r)));
    return r;
  };
  D3D12_HEAP_PROPERTIES heap{};
  heap.Type = D3D12_HEAP_TYPE_DEFAULT;
  D3D12_RESOURCE_DESC texture{};
  texture.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
  texture.Width = 64;
  texture.Height = 1;
  texture.DepthOrArraySize = 1;
  texture.MipLevels = 1;
  texture.SampleDesc.Count = 1;
  texture.Format = DXGI_FORMAT_R32G32B32A32_FLOAT;
  texture.Flags = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;
  ComPtr<ID3D12Resource> gpu;
  ok(device->CreateCommittedResource(&heap, D3D12_HEAP_FLAG_NONE, &texture,
                                     D3D12_RESOURCE_STATE_RENDER_TARGET,
                                     nullptr, IID_PPV_ARGS(&gpu)));
  D3D12_DESCRIPTOR_HEAP_DESC desc{};
  desc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
  desc.NumDescriptors = 1;
  ComPtr<ID3D12DescriptorHeap> rtvs;
  ok(device->CreateDescriptorHeap(&desc, IID_PPV_ARGS(&rtvs)));
  auto rtv = rtvs->GetCPUDescriptorHandleForHeapStart();
  device->CreateRenderTargetView(gpu.Get(), nullptr, rtv);
  auto readback =
      buffer(D3D12_HEAP_TYPE_READBACK, D3D12_RESOURCE_STATE_COPY_DEST,
             D3D12_RESOURCE_FLAG_NONE);
  ComPtr<ID3D12CommandQueue> queue;
  D3D12_COMMAND_QUEUE_DESC q{};
  q.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
  ok(device->CreateCommandQueue(&q, IID_PPV_ARGS(&queue)));
  ComPtr<ID3D12CommandAllocator> allocator;
  ok(device->CreateCommandAllocator(q.Type, IID_PPV_ARGS(&allocator)));
  ComPtr<ID3D12GraphicsCommandList> list;
  ok(device->CreateCommandList(0, q.Type, allocator.Get(), pipeline.Get(),
                               IID_PPV_ARGS(&list)));
  list->SetGraphicsRootSignature(root.Get());
  unsigned values[4] = {(unsigned)atoi(argv[3]), 0, 0, 0};
  list->SetGraphicsRoot32BitConstants(0, 4, values, 0);
  list->OMSetRenderTargets(1, &rtv, FALSE, nullptr);
  D3D12_VIEWPORT vp{0, 0, 64, 1, 0, 1};
  D3D12_RECT scissor{0, 0, 64, 1};
  list->RSSetViewports(1, &vp);
  list->RSSetScissorRects(1, &scissor);
  list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
  ComPtr<ID3D12Resource> vertices;
  if (tangents) {
    vertices = buffer(D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_STATE_GENERIC_READ,
                      D3D12_RESOURCE_FLAG_NONE);
    unsigned authored[3][8] = {{12, 160, 220, 85, 200, 40, 120, 250},
                              {12, 160, 220, 85, 200, 40, 120, 250},
                              {12, 160, 220, 85, 200, 40, 120, 250}};
    void *mapped;
    D3D12_RANGE empty{0, 0};
    ok(vertices->Map(0, &empty, &mapped));
    memcpy(mapped, authored, sizeof authored);
    vertices->Unmap(0, nullptr);
    D3D12_VERTEX_BUFFER_VIEW view{vertices->GetGPUVirtualAddress(),
                                 sizeof authored, sizeof authored[0]};
    list->IASetVertexBuffers(0, 1, &view);
  }
  list->DrawInstanced(3, 1, 0, 0);
  D3D12_RESOURCE_BARRIER barrier{};
  barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
  barrier.Transition = {gpu.Get(), D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,
                        D3D12_RESOURCE_STATE_RENDER_TARGET,
                        D3D12_RESOURCE_STATE_COPY_SOURCE};
  list->ResourceBarrier(1, &barrier);
  D3D12_TEXTURE_COPY_LOCATION dest{}, src{};
  dest.pResource = readback.Get();
  dest.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
  dest.PlacedFootprint.Footprint = {DXGI_FORMAT_R32G32B32A32_FLOAT, 64, 1, 1,
                                    1024};
  src.pResource = gpu.Get();
  src.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
  list->CopyTextureRegion(&dest, 0, 0, 0, &src, nullptr);
  ok(list->Close());
  ID3D12CommandList *lists[] = {list.Get()};
  queue->ExecuteCommandLists(1, lists);
  ComPtr<ID3D12Fence> fence;
  ok(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence)));
  HANDLE event = CreateEvent(nullptr, FALSE, FALSE, nullptr);
  if (!event)
    throw std::runtime_error("CreateEvent failed");
  ok(queue->Signal(fence.Get(), 1));
  ok(fence->SetEventOnCompletion(1, event));
  if (WaitForSingleObject(event, 30000) != WAIT_OBJECT_0)
    throw std::runtime_error("GPU timeout");
  CloseHandle(event);
  void *data;
  D3D12_RANGE range{0, bytes};
  ok(readback->Map(0, &range, &data));
  write(argv[2], data, bytes);
  D3D12_RANGE empty{0, 0};
  readback->Unmap(0, &empty);
  puts("Authored graphics readback complete");
  return 0;
} catch (const std::exception &e) {
  fprintf(stderr, "%s\n", e.what());
  return 1;
}
