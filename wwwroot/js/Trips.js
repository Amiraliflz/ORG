function generateTripCard(Model) {
  const isVipCar = Model.taxiSupervisorID === 7 ||
    (Model.carModelName && (
      Model.carModelName.includes('VIP') ||
      Model.carModelName.includes('vip') ||
      Model.carModelName.includes('تشریفات') ||
      Model.carModelName.includes('آریو') ||
      Model.carModelName.includes('اکسنت') ||
      Model.carModelName.includes('جیلی') ||
      Model.carModelName.includes('کمری') ||
      Model.carModelName.includes('سفران') ||
      Model.carModelName.includes('سوناتا')
    ));

  let imageUrl = '/taxi.webp';
  const imageValue = Model.Image || Model.image;
  if (imageValue && typeof imageValue === 'string' && imageValue.trim() !== '') {
    const imagePath = imageValue.trim();
    imageUrl = imagePath.startsWith('/')
      ? 'https://mrbilit.mrshoofer.ir' + imagePath
      : imagePath;
  }

  const showOrgPrice = Model.originalPrice && Model.afterdiscount &&
    String(Model.originalPrice) !== String(Model.afterdiscount);

  return `
    <article class="trip-card card mt-3">
      <div class="trip-card-body">
        <div class="trip-card-meta">
          ${isVipCar ? '<span class="trip-card-vip">وی‌آی‌پی</span>' : '<span class="trip-card-type">دربستی</span>'}
          <span class="trip-card-capacity" title="ظرفیت ۳">
            <i class="ti ti-user" aria-hidden="true"></i>
            ۳
          </span>
          <span class="trip-card-car">${Model.carModelName || ''}</span>
        </div>

        <div class="trip-card-main">
          <div class="trip-card-provider">
            <img class="trip-card-logo"
                 src="${imageUrl}"
                 width="44" height="44"
                 alt="${Model.taxiSupervisorName || ''}"
                 loading="lazy" decoding="async"
                 onerror="this.src='/taxi.webp'" />
            <span class="trip-card-provider-name">${Model.taxiSupervisorName || ''}</span>
          </div>

          <div class="trip-card-route">
            <div class="trip-card-endpoint trip-card-endpoint--start">
              <span class="trip-card-time starttime">${Model.startingDateTime || ''}</span>
              <span class="trip-card-place directionval">${Model.origin || ''}</span>
            </div>

            <div class="trip-card-rail" aria-hidden="true">
              <div class="trip-card-rail-track">
                <span class="trip-card-rail-dot trip-card-rail-dot--solid"></span>
                <span class="trip-card-rail-line"></span>
                <span class="trip-card-rail-mid">
                  <i class="ti ti-car trip-card-rail-icon"></i>
                  ${Model.travelDuration ? `<span class="trip-card-rail-duration">${Model.travelDuration}</span>` : ''}
                </span>
                <span class="trip-card-rail-line"></span>
                <span class="trip-card-rail-dot trip-card-rail-dot--hollow"></span>
              </div>
            </div>

            <div class="trip-card-endpoint trip-card-endpoint--end">
              <span class="trip-card-time-row">
                ${Model.arrivesNextDay ? '<span class="badge bg-label-secondary rounded-pill nextday-badge">روز بعد</span>' : ''}
                <span class="trip-card-time endtime">${Model.arrivalDateTime || ''}</span>
              </span>
              <span class="trip-card-place directionval">${Model.destination || ''}</span>
            </div>
          </div>
        </div>
      </div>

      <div class="trip-card-divider" aria-hidden="true"></div>

      <div class="trip-card-action">
        <div class="trip-card-prices">
          ${showOrgPrice ? `<span class="trip-card-orgprice">${Model.originalPrice}</span>` : ''}
          <span class="trip-card-price">
            ${Model.afterdiscount || ''}
            <span class="toman" aria-hidden="true">
              <svg class="toman" width="14" height="14" viewBox="0 0 14 14" xmlns="http://www.w3.org/2000/svg">
                <path clip-rule="evenodd" fill-rule="evenodd" fill="currentColor" d="M3.057 1.742L3.821 1l.78.75-.776.741-.768-.749zm3.23 2.48c0 .622-.16 1.111-.478 1.467-.201.221-.462.39-.783.505a3.251 3.251 0 01-1.083.163h-.555c-.421 0-.801-.074-1.139-.223a2.045 2.045 0 01-.9-.738A2.238 2.238 0 011 4.148c0-.059.001-.117.004-.176.03-.55.204-1.158.525-1.827l1.095.484c-.257.532-.397 1-.419 1.403-.002.04-.004.08-.004.12 0 .252.055.458.166.618a.887.887 0 00.5.354c.085.028.178.048.278.06.079.01.16.014.243.014h.555c.458 0 .769-.081.933-.244.14-.139.21-.383.21-.731V2.02h1.2v2.202zm5.433 3.184l-.72-.7.709-.706.735.707-.724.7zm-2.856.308c.542 0 .973.19 1.293.569.297.346.445.777.445 1.293v.364h.18v-.004h.41c.221 0 .377-.028.467-.084.093-.055.14-.14.14-.258v-.069c.004-.243.017-1.044 0-1.115L13 8.05v1.574a1.4 1.4 0 01-.287.863c-.306.405-.804.607-1.495.607h-.627c-.061.733-.434 1.257-1.117 1.573-.267.122-.58.21-.937.265a5.845 5.845 0 01-.914.067v-1.159c.612 0 1.072-.082 1.38-.247.25-.132.376-.298.376-.499h-.515c-.436 0-.807-.113-1.113-.339-.367-.273-.55-.667-.55-1.18 0-.488.122-.901.367-1.24.296-.415.728-.622 1.296-.622zm.533 2.226v-.364c0-.217-.048-.389-.143-.516a.464.464 0 00-.39-.187.478.478 0 00-.396.187.705.705 0 00-.136.449.65.65 0 00.003.067c.008.125.066.22.177.283.093.054.21.08.352.08h.533zM9.5 6.707l.72.7.724-.7L10.209 6l-.709.707zm-6.694 4.888h.03c.433-.01.745-.106.937-.29.024.012.065.035.12.068l.074.039.081.042c.135.073.261.133.379.18.345.146.67.22.977.22a1.216 1.216 0 00.87-.34c.3-.285.449-.714.449-1.286a2.19 2.19 0 00-.335-1.145c-.299-.457-.732-.685-1.3-.685-.502 0-.916.192-1.242.575-.113.132-.21.284-.294.456-.032.062-.06.125-.084.191a.504.504 0 00-.03.078 1.67 1.67 0 00-.022.06c-.103.309-.171.485-.205.53-.072.09-.214.14-.427.147-.123-.005-.209-.03-.256-.076-.057-.054-.085-.153-.085-.297V7l-1.201-.5v3.562c0 .261.048.496.143.703.071.158.168.296.29.413.123.118.266.211.43.28.198.084.42.13.665.136v.001h.036zm2.752-1.014a.778.778 0 00.044-.353.868.868 0 00-.165-.47c-.1-.134-.217-.201-.35-.201-.18 0-.33.103-.447.31-.042.071-.08.158-.114.262a2.434 2.434 0 00-.04.12l-.015.053-.015.046c.142.118.323.216.544.293.18.062.325.092.433.092.044 0 .086-.05.125-.152z"></path>
              </svg>
            </span>
          </span>
        </div>
        <a href="/Reserve/Reservetrip?tripcode=${Model.tripcode}" class="btn btn-primary trip-card-book">
          رزرو سفر
        </a>
        <span class="trip-card-action-note">دربستی</span>
      </div>
    </article>`;
}


function fetchTripsData() {
  let origin_city = $("#origin_input").val();
  let destination_city = $("#destination_input").val();
  let searchdate = $("#starttime").val();

  return new Promise((resolve, reject) => {
    $.ajax({
      url: `/TaxiTrips/SearchJson?originstring=${encodeURIComponent(origin_city || '')}&destinationstring=${encodeURIComponent(destination_city || '')}&searchdate=${encodeURIComponent(searchdate || '')}`,
      method: 'GET',
      dataType: 'json',
      success: function (response) {
        if (Array.isArray(response) && response.length > 0) {
          resolve(response);
        } else if (response && response.error) {
          reject({ responseJSON: response });
        } else {
          resolve([]);
        }
      },
      error: function (xhr, status, error) {
        reject(xhr);
      }
    });
  });
}


let nottripfoundHtml = `<div class="d-flex col-12 mt-3" style="flex-direction: column; align-items: center; justify-content: start;">

							<label class="fs-4 fw-bold mt-4 pt-3">
							  ســفری یافت نشـد
							</label>
							<small>
							  در بازه ای که شما جست و جو کردید، سفری یافت نشد
							</small>
							</div>

           <div class="trips-container">
        </div>`;



// Avoid redeclaration error: only create if undefined
if (typeof window.trips === 'undefined') {
  window.trips = [];
}

function GetCarModels(tripsarr) {
  const carModelNames = tripsarr.map(trip => trip.carModelName);
  const uniqueCarModels = [...new Set(carModelNames)];
  return uniqueCarModels;
}

function carFilterSelectes(carmodel) {
  if (carmodel == 'default') {
    renderTrips(window.trips);
  } else {
    const filteredTrips = window.trips.filter(t => t.carModelName === carmodel);
    renderTrips(filteredTrips);
  }
}

function GenerateCarModelsFilter(carmodels) {
  const $container = $("#carmodelsfilter");
  if ($container.length === 0) return;

  $container.empty();
  $container.addClass("d-flex flex-wrap gap-2");

  $container.append(
    '<button type="button" class="btn btn-sm btn-secondary text-white rounded-pill car-chip" data-carmodel="default">همه</button>'
  );

  carmodels.forEach(c => {
    const safeText = String(c).replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const isVipCar = c && (c.includes('VIP') || c.includes('vip') || c.includes('تشریفات') ||
      c.includes('آریو') || c.includes('اکسنت') || c.includes('جیلی') ||
      c.includes('کمری') || c.includes('سفران') || c.includes('سوناتا'));
    const vipBadge = isVipCar ? '<img src="/vip_badge.png" style="height:16px;" class="ms-1" alt="" />' : '';
    $container.append(
      `<button type="button" class="btn btn-sm btn-outline-secondary rounded-pill car-chip" data-carmodel="${safeText}">${safeText}${vipBadge}</button>`
    );
  });
}

$(function () {
  $(document).on('click', '#carmodelsfilter .car-chip', function () {
    const model = $(this).data('carmodel');

    $('#carmodelsfilter .car-chip')
      .removeClass('btn-secondary text-white')
      .addClass('btn-outline-secondary');

    $(this)
      .removeClass('btn-outline-secondary')
      .addClass('btn-secondary text-white');

    carFilterSelectes(model);
  });

  fetchTripsData()
    .then(function (result) {
      window.trips = result;

      if (window.trips.length == 0) {
        $('.trips-container').empty();
        $('.trips-container').append(nottripfoundHtml);
      } else {
        renderTrips(window.trips);
        let carModels = GetCarModels(window.trips);
        GenerateCarModelsFilter(carModels);
      }
    })
    .catch(function (error) {
      console.error('Error fetching data:', error);
      const msg = (error && error.responseJSON && error.responseJSON.error)
        ? error.responseJSON.error
        : 'خطا در بارگذاری سفرها. لطفا دوباره تلاش کنید.';
      $('.trips-container').empty().append(`<div class="d-flex col-12 mt-3" style="flex-direction: column; align-items: center; justify-content: start;">
        <label class="fs-5 fw-bold mt-4 pt-3 text-danger">${msg}</label>
      </div>`);
    });
});

function renderTrips(input_trips) {
  $('.trips-container').empty();
  input_trips.forEach(t => { $('.trips-container').append(generateTripCard(t)) });
}

function orderFilterSelected(number) {
  if (number == 0) {
    renderTrips(window.trips);
  }

  if (number == 1) {
    const deccendingPrice_trips = [...window.trips]
      .sort((a, b) =>
        parseInt(b.afterdiscount.replace(/,/g, ""), 10) - parseInt(a.afterdiscount.replace(/,/g, ""), 10)
      );
    renderTrips(deccendingPrice_trips);
  } else if (number == 2) {
    const accendingPrice_trips = [...window.trips]
      .sort((a, b) =>
        parseInt(a.afterdiscount.replace(/,/g, ""), 10) - parseInt(b.afterdiscount.replace(/,/g, ""), 10)
      );
    renderTrips(accendingPrice_trips);
  }
}
