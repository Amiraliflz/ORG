function generateTripCard(Model) {

  var html = `
        <div class="card px-2 px-md-4 py-3 mt-3" style="width: 100%;">

        <div class="mainbody row">
            <div class="topinfo d-flex" style="justify-content: space-between; align-items:center;">

                <div>
                    <span class="badge bg-label-secondary rounded-pill carmodel"> ${Model.carModelName} </span>
                    ${Model.taxiSupervisorID === 7 ? '<img src="/vip_badge.png" style="height:29px;" class="ms-1" />' : ''}
                    
                </div>

                <div>

                    <div class="capacity badge bg-label-secondary rounded-pill">
                        <i class="ti ti-armchair-2 d-inline"></i>
                        |
                        <small class="text-danger ms-0 ms-md-1 d-inline">دربستی</small>
                    </div>
                </div>
            </div>

            <div class="trip-info mt-3 ms-2">

              <div class="d-flex justify-content-between">

                <div class="provider">
                    <img class="logo-image" src="/taxi.png" height="40px" alt="Logo">
                    <h5 class="mt-1">
                        ${Model.taxiSupervisorName}
                    </h5>
                </div>

                <div class="transport">
                    <div class="direction-container">

                        <div class="startendtime me-1" style="display: flex; justify-content:space-between; align-items: center; ">

                            <span class="badge bg-label-secondary rounded-pill starttime" style="border-radius: 15% !important;">
                              <span class="starttime" style="color: black;">
                                  ${Model.startingDateTime}
                               </span>
                            </span>

                            <div>
                                <span class="">
                                   ...<span>
                                        <svg data-v-7c8d09eb="" class="fa-taxiSide" aria-hidden="true" focusable="false" data-prefix="fa-mrbilit" data-icon="taxiSide" role="img" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" style="color: var(--cool-gray-400);"><path class="" fill="currentColor" d="M480.6,179.2l-90.3-57.5c-7-4.5-14.8-7.5-23-8.8c-3.1-0.5-6.3-0.7-9.5-0.7h-68.5v-9.7c0-6.4-2.6-12.6-7.1-17.2  c-4.6-4.6-10.7-7.1-17.2-7.1c-6.4,0-12.6,2.6-17.2,7.1c-4.6,4.6-7.1,10.7-7.1,17.2v9.7h-6.7c-7.9,0-15.7,1.5-23,4.6  c-7.3,3-13.9,7.5-19.5,13.1l-62.3,61.6c-1.4,1.5-3.2,2.6-5.2,3.2c-0.2,0-0.3,0.1-0.5,0.1c-1.4,0.3-3.2,0.8-5.3,1.4l-32,10.6  c-3.7,1.2-7.7,2.4-12,3.5c-15.6,4.4-33.2,9.3-47.9,22.2C9.6,247.3,0,269.3,0,293.1V320c0,32.9,27,59.6,60.2,59.6h17.5  c3.2,15.3,11.7,29.1,23.9,39c12.2,9.9,27.4,15.3,43.1,15.3c15.7,0,30.9-5.4,43.1-15.3c12.2-9.9,20.6-23.6,23.9-39h88.9  c3.2,15.3,11.7,29.1,23.9,39c12.2,9.9,27.4,15.3,43.1,15.3s30.9-5.4,43.1-15.3c12.2-9.9,20.6-23.6,23.9-39h17.4  c33.2,0,60.2-26.7,60.2-59.6v-77.3C512,223.3,502.2,191.3,480.6,179.2z M456.2,221.3l0.2,0.1c0,0.1,0.1,0.2,0.1,0.2h-72.3v-46.1  L456.2,221.3z M225.7,164.3c1.1-1.2,2.4-2.1,3.8-2.7c1.4-0.6,3-0.9,4.6-0.9h101.6v60.9h-94.3c-20.6,0-40.9-4.9-59.3-14.2  L225.7,164.3z M144.6,385.3c-3.9,0-7.8-1.2-11-3.3c-3.3-2.2-5.8-5.3-7.3-8.9c-1.5-3.6-1.9-7.6-1.1-11.5  c0.8-3.9,2.7-7.4,5.4-10.2  c2.8-2.8,6.3-4.7,10.2-5.4c3.9-0.8,7.9-0.4,11.5,1.1c3.6,1.5,6.7,4.1,8.9,7.3c2.2,3.3,3.3,7.1,3.3,11  c0,5.3-2.1,10.3-5.8,14.1  C154.9,383.2,149.9,385.3,144.6,385.3z M367.4,385.3c-3.9,0-7.8-1.2-11-3.3c-3.3-2.2-5.8-5.3-7.3-8.9c-1.5-3.6-1.9-7.6-1.1-11.5  c0.8-3.9,2.7-7.4,5.4-10.2c2.8-2.8,6.3-4.7,10.2-5.4c3.9-0.8,7.9-0.4,11.5,1.1c3.6,1.5,6.7,4.1,8.9,7.3c2.2,3.3,3.3,7.1,3.3,11  c0,5.3-2.1,10.3-5.8,14.1C377.7,383.2,372.7,385.3,367.4,385.3L367.4,385.3z M451.8,331h-25.2c-6-10.4-14.6-19-25-24.9  c-10.4-6-22.1-9.1-34.1-9.1c-12,0-23.7,3.1-34.1,9.1c-10.4,6-19,14.6-25,24.9H203.7c-6-10.4-14.6-18.9-25-24.9  c-10.4-6-22.1-9.1-34.1-9.1c-12,0-23.7,3.1-34.1,9.1c-10.4,6-19,14.6-25,24.9H60.2c-6.4,0-11.7-5-11.7-11.1v-26.9  c0-9.6,3.7-18.6,9.9-24.1c6.4-5.6,17.4-8.7,29-11.9c4.6-1.3,9.4-2.6,14.2-4.2l31.9-10.6c0.2-0.1,0.5-0.1,0.7-0.2  c0.8-0.2,1.6-0.4,2.5-0.6c1.7-0.5,3.4-1,5.1-1.6c29.5,19.7,64.2,30.2,99.7,30.3h222V320C463.4,326.1,458.2,331,451.8,331L451.8,331z"></path></svg>
                                    </span>...
                                </span>
                            </div>

                            <span class="endtime ms-1">

                            ${Model.arrivalDateTime}
								
                            </span>
                            <span class="text-muted" style="font-size:.5rem;">(تقریبی)</span>
                        </div>

                        <div class="direction" style="display:flex; justify-content:space-between;">
                            <span class="directionval">
                            ${Model.origin}
                                
                            </span>


                            <span class="directionval">
                               ${Model.destination}
                            </span>


                        </div>
                    </div>

                </div>
              </div>

                <div class="submitation d-flex" style="flex-direction: column; justify-content:center; ">

                    <div class="prices mb-1 d-flex" style= "align-items: center; flex-direction: column;">
                        <span class="orgprice" style="font-size:10px;">
                            ${Model.originalPrice}
                            <span class="badge bg-label-secondary rounded-pill" style="color:black !important; padding: 2px;">20%</span>
                        </span>

                        <span class="price d-block">
                        ${Model.afterdiscount}
                            <span class="toman">
                                <svg class="toman">
                                    <use xlink:href="#toman">
                                        <symbol id="toman" viewBox="0 0 14 14" xmlns="http://www.w3.org/2000/svg">
                                            <path clip-rule="evenodd" d="M3.057 1.742L3.821 1l.78.75-.776.741-.768-.749zm3.23 2.48c0 .622-.16 1.111-.478 1.467-.201.221-.462.39-.783.505a3.251 3.251 0 01-1.083.163h-.555c-.421 0-.801-.074-1.139-.223a2.045 2.045 0 01-.9-.738A2.238 2.238 0 011 4.148c0-.059.001-.117.004-.176.03-.55.204-1.158.525-1.827l1.095.484c-.257.532-.397 1-.419 1.403-.002.04-.004.08-.004.12 0 .252.055.458.166.618a.887.887 0 00.5.354c.085.028.178.048.278.06.079.01.16.014.243.014h.555c.458 0 .769-.081.933-.244.14-.139.21-.383.21-.731V2.02h1.2v2.202zm5.433 3.184l-.72-.7.709-.706.735.707-.724.7zm-2.856.308c.542 0 .973.19 1.293.569.297.346.445.777.445 1.293v.364h.18v-.004h.41c.221 0 .377-.028.467-.084.093-.055.14-.14.14-.258v-.069c.004-.243.017-1.044 0-1.115L13 8.05v1.574a1.4 1.4 0 01-.287.863c-.306.405-.804.607-1.495.607h-.627c-.061.733-.434 1.257-1.117 1.573-.267.122-.58.21-.937.265a5.845 5.845 0 01-.914.067v-1.159c.612 0 1.072-.082 1.38-.247.25-.132.376-.298.376-.499h-.515c-.436 0-.807-.113-1.113-.339-.367-.273-.55-.667-.55-1.18 0-.488.122-.901.367-1.24.296-.415.728-.622 1.296-.622zm.533 2.226v-.364c0-.217-.048-.389-.143-.516a.464.464 0 00-.39-.187.478.478 0 00-.396.187.705.705 0 00-.136.449.65.65 0 00.003.067c.008.125.066.22.177.283.093.054.21.08.352.08h.533zM9.5 6.707l.72.7.724-.7L10.209 6l-.709.707zm-6.694 4.888h.03c.433-.01.745-.106.937-.29.024.012.065.035.12.068l.074.039.081.042c.135.073.261.133.379.18.345.146.67.22.977.22a1.216 1.216 0 00.87-.34c.3-.285.449-.714.449-1.286a2.19 2.19 0 00-.335-1.145c-.299-.457-.732-.685-1.3-.685-.502 0-.916.192-1.242.575-.113.132-.21.284-.294.456-.032.062-.06.125-.084.191a.504.504 0 00-.03.078 1.67 1.67 0 00-.022.06c-.103.309-.171.485-.205.53-.072.09-.214.14-.427.147-.123-.005-.209-.03-.256-.076-.057-.054-.085-.153-.085-.297V7l-1.201-.5v3.562c0 .261.048.496.143.703.071.158.168.296.29.413.123.118.266.211.43.28.198.084.42.13.665.136v.001h.036zm2.752-1.014a.778.778 0 00.044-.353.868.868 0 00-.165-.47c-.1-.134-.217-.201-.35-.201-.18 0-.33.103-.447.31-.042.071-.08.158-.114.262a2.434 2.434 0 00-.04.12l-.015.053-.015.046c.142.118.323.216.544.293.18.062.325.092.433.092.044 0 .086-.05.125-.152z" fill-rule="evenodd"></path>
                                        </symbol>
                                    </use>
                                </svg>
                            </span>
                        </span>
                    </div>

                    <a href="/Reserve/Reservetrip?tripcode=${Model.tripcode}" class="btn btn-primary waves-effect waves-light mt-1">رزور سفر

                        <i class="ti ti-arrow-left ms-2"></i>
                    </a>

                </div>
            </div>

        </div>
    </div>`;

  // Return the generated HTML
  return html;
}


function fetchTripsData() {
  let origin_city = $("#origin_input").val();
  let destination_city = $("#destination_input").val();
  let searchdate = $("#starttime").val();




  return new Promise((resolve, reject) => {
    $.ajax({
      url: `/TaxiTrips/SearchJson?originstring=${origin_city}&destinationstring=${destination_city}&searchdate=${searchdate}`, // Replace with your API endpoint
      method: 'GET',
      dataType: 'json',
      success: function (response) {
        if (Array.isArray(response) && response.length > 0) {
          resolve(response); // Resolve with the result
        } else {
          resolve([]); // Resolve with an empty array if no data
        }
      },
      error: function (xhr, status, error) {
        reject(error); // Reject the promise with an error
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

  // Step 2: Get unique car models using a Set
  const uniqueCarModels = [...new Set(carModelNames)];


  return uniqueCarModels;
}



function carFilterSelectes(carmodel) {

  if (carmodel == 'default') {
    renderTrips(window.trips);

  }
  else {

    const filteredTrips = window.trips.filter(t => t.carModelName === carmodel)

  renderTrips(filteredTrips);
  }
  

}


function GenerateCarModelsFilter(carmodels) {
  // Render Bootstrap-like chips that stay in place
  const $container = $("#carmodelsfilter");
  if ($container.length === 0) return;

  // Clear existing
  $container.empty();

  // Keep chips in a row with modest spacing, no separators
  $container.addClass("d-flex flex-wrap gap-2");

  // Add 'All' chip
  $container.append(
    '<button type="button" class="btn btn-sm btn-secondary text-white rounded-pill car-chip" data-carmodel="default">همه</button>'
  );

  // Add a chip per unique car model
  carmodels.forEach(c => {
    const safeText = String(c).replace(/</g, "&lt;").replace(/>/g, "&gt;");
    $container.append(
      `<button type="button" class="btn btn-sm btn-outline-secondary rounded-pill car-chip" data-carmodel="${safeText}">${safeText}</button>`
    );
  });
}

$(function () {

  // Delegate click handling for chips without moving them or re-rendering the container
  $(document).on('click', '#carmodelsfilter .car-chip', function () {
    const model = $(this).data('carmodel');

    // Visual active state without layout shift
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

      // no trips
      if (window.trips.length == 0) {
        $('.trips-container').empty();
        $('.trips-container').append(nottripfoundHtml);
      }
      else {

        renderTrips(window.trips);
        let carModels = GetCarModels(window.trips);
        GenerateCarModelsFilter(carModels);

      }
    })
    .catch(function (error) {
      console.error('Error fetching data:', error); // Handle the error here
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

  // Filter by price high to low
  if (number == 1) {
    const deccendingPrice_trips = [...window.trips]
      .sort((a, b) =>
        parseInt(b.afterdiscount.replace(/,/g, ""), 10) - parseInt(a.afterdiscount.replace(/,/g, ""), 10)
      );
    renderTrips(deccendingPrice_trips);

  }
  else if (number == 2) {
    const accendingPrice_trips = [...window.trips]
      .sort((a, b) =>
        parseInt(a.afterdiscount.replace(/,/g, ""), 10) - parseInt(b.afterdiscount.replace(/,/g, ""), 10)
      );
    renderTrips(accendingPrice_trips);
  }
}

