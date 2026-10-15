#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Devices.Geolocation.h>
#include <cstdio>
#include <chrono>
int main() {
 try {
  winrt::init_apartment(winrt::apartment_type::multi_threaded);
  winrt::Windows::Devices::Geolocation::Geolocator locator;
  locator.DesiredAccuracy(winrt::Windows::Devices::Geolocation::PositionAccuracy::High);
  auto position=locator.GetGeopositionAsync(std::chrono::seconds(0),std::chrono::seconds(15)).get();
  auto coordinate=position.Coordinate(); auto p=coordinate.Point().Position();
  std::printf("{\"latitude\":%.10f,\"longitude\":%.10f,\"altitude\":%.3f,\"accuracyMeters\":%.3f,\"positionSource\":%d,\"timestampTicks\":%lld}\n",p.Latitude,p.Longitude,p.Altitude,coordinate.Accuracy(),(int)coordinate.PositionSource(),(long long)coordinate.Timestamp().time_since_epoch().count());
  return 0;
 } catch(winrt::hresult_error const& error) {
  std::fprintf(stderr,"Geolocator failed: HRESULT=0x%08X %s\n",(unsigned int)error.code().value,winrt::to_string(error.message()).c_str()); return 1;
 }
}
